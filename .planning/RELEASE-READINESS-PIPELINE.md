# Release pipeline readiness — 2026-09-10

Preparation for 1.0.0, based on dev `21648697` (ModBuild 497, package version
0.9.1). No version bump, tag, release, branch push, repository visibility change,
or GitHub workflow execution was performed in this review.

## Provenance and release flow

Source review of `.github/workflows/release.yml`, `ci.yml`, release simulation,
packagers and `Core/SelfUpdate` found the intended main-only flow intact:

- Only a push to `main` starts the release workflow. The committed version supplies
  the tag and package name. An existing tag stops the run before publication.
- The workflow verifies that its exact SHA is reachable from `dev`, checks the
  tracked tree before/after building, and rechecks reachability from current `main`
  immediately before creating the tag at that SHA.
- Release builds set `GhvrReleaseBuild=true`; the workflow publishes the verified
  tag and full ZIP as Latest. The next patch bump is a subsequent commit on `dev`,
  with retry handling for a concurrent push. Main receives no release-bot bump.
- Hosted checks run the native presentation harnesses; the wire harness is compiled
  there. Its full execution still requires local Unity references. The integrator
  owns the complete current local gates before the eventual main merge.

No workflow changes were necessary. `scripts/release-sim.sh`, `--race` and `--old`
passed in isolated scratch repositories: three consecutive modern releases stay
fast-forwardable; the racing duplicate is rejected and recoverable; the old design
reproduces the expected divergence. These simulations do not test GitHub tokens,
repository settings, asset uploads or live update downloads.

## Concrete fixes

1. `install.ps1` now XML-escapes `GameManaged` when generating local build properties.
   A valid install path containing `&` previously produced invalid XML.
2. `uninstall.ps1` still restores backups before removal, but a failed optional
   shader-verification build/runtime no longer prevents uninstall. Verification uses
   the repository SDK context and Major runtime roll-forward, matching install.
   Actual backup restoration errors remain fatal; BepInEx and user configuration stay.
3. Both packagers now include GPL `LICENSE.txt` and pinned Unity XR/OpenXR copyright,
   licence and third-party notices beneath `BepInEx/plugins/GloomhavenVR/`. Existing
   updater path rules already permit this layout. Licence texts use UTF-8 BOM/CRLF
   in the package, like every other shipped TXT. Package checks require the licence
   and notice index. No source/game assemblies were added.
4. The generated release page now states BepInEx as a prerequisite without also
   claiming that nothing else needs downloading. Unpublished player highlights are
   prepared in `packaging/release-highlights/1.0.0.md`.

## Local validation

- `GhvrReleaseBuild=true bash scripts/package-release.sh`: build succeeded with
  0 warnings/errors; ZIP layout and all 12 TXT encodings passed. Generated build
  identity is version `0.9.1`, commit `2164869`, `IsDevBuild=false`.
- Actual package: 29 entries, 85,043,182 bytes compressed; original bundle, two mod
  DLLs, three Unity managed dependencies, two OpenXR natives, versions manifest,
  installation guides and notices. No game DLLs, source or development outputs.
- A temporary console project linked the unchanged production `SelfUpdateZip` and
  `SelfUpdateRelease` sources. Actual ZIP Verify/Extract passed, every extracted file
  matched its ZIP entry by SHA-256, incorrect byte size was rejected, draft metadata
  was rejected, and release metadata for `v1.0.0` compared newer than `0.9.1`.
  This net8 fixture emitted two existing nullable warnings in the reflection helper;
  the actual configured plugin build was clean. No download/apply/restart was tested.
- PowerShell 7 isolated fake installations: failed verifier build, original/patched/
  failed verification, no backup and no dotnet all completed cleanup correctly.
  Backups and boot.config restored; BepInEx/config retained; caller location and
  native-error preference preserved. No real game files were touched.
- Production installer property-writing block parsed a `D:\Games & Tools` path back
  unchanged. Its production licence-deployment block produced all nine licence
  TXT files with correct content/BOM/CRLF. Both full PowerShell scripts parsed.
- Bash syntax and `git diff --check` passed. Release notes rendered successfully
  for 1.0.0 against previous tag v0.9.0; this is an unpublished draft.

Temporary fixtures/logs: `/tmp/release100-script-check.py`,
`/tmp/release100-script-check.log`, `/tmp/release100-updater/`,
`/tmp/release100-package-final.log`, `/tmp/release100-sim*.log`, and
`/tmp/release100-draft-notes.md`. They are local evidence, not repository dependencies.

## Notice provenance and remaining external work

Package-specific notice content was compared with committed upstream files in the
exact source checkouts used for the runtime build. Copies are byte-identical except
for one removed trailing blank line in the OpenXR LICENSE. Hashes below identify
the original upstream files:

| Package | Pinned revision | LICENSE.md SHA-256 |
| --- | --- | --- |
| XR Management 4.5.0 | `4bed75a6b044755dc2854fee33a20119c792fb5e` | `ca732e9631e04a74944880296b67f9cb5b63cbcf03d3481afb520ac0dc1286e8` |
| XR Core Utils 2.2.3 | `c25ad69335f74ba9abf91e10a141badde91dfc43` | `729d2121f79da7d543602c8dbafe84779dcfdaa203156f3dac9bd0605c973818` |
| OpenXR 1.10.0 | `dc3a991d6f01595011b254cc9379d0b9fb00aab2` | `d6bf7693f27eeb04d023a2686299aed0e29f38083d41f22fce7b670a4c64f35f` |

OpenXR `Third Party Notices.md` SHA-256:
`9eb9e8d5c757ce3e6c4748090444ea9854f6b9419727f559939fda2d4023da6b`.
`packaging/licenses/SOURCES.txt` records source links and native versions. Referenced
texts were read from the publishers: [Unity Companion License v1.4](https://unity.com/legal/licenses/unity-companion-license),
[Unity Package Distribution License v2.1](https://unity.com/legal/licenses/unity-package-distribution-license)
and [Apache 2.0](https://www.apache.org/licenses/LICENSE-2.0.txt). Supplying these notices
preserves upstream terms; it does not settle project-wide redistribution rights.

The user explicitly deferred the fire-asset licensing question on 2026-09-10. The
asset and existing fire notice remain unchanged, including the entire restricted
asset section. That publication item belongs to the user; this review did not
resolve it. The integrator is reviewing other public-source readiness separately.

Windows PowerShell 5.1 on a real install, an actual main-triggered hosted release,
public Latest download, updater application/restart/rollback, and headset behavior
remain untested here. The outstanding four-player hardware performance test and
unexplained one-off long-rest board disappearance remain in the draft highlights.

## Integrated follow-up: stale local bundle selection

The main checkout exposed a packaging defect absent from the clean worker checkout:
its ignored Unity `Build/Bundles/gloomhavenvr.bundle` was an older 68,522,833-byte
asset set, while committed `prebuilt/gloomhavenvr.bundle` is 74,943,763 bytes. Both
packagers treated existence of local output as evidence that it was fresher, silently
replacing the release asset set with old content.

Both scripts now select committed `prebuilt` by default. Developers can explicitly
choose local Unity output using `GHVR_USE_LOCAL_BUNDLE=1` for package-release.sh or
`-UseLocalBundle` for install.ps1. A missing explicitly requested local file fails
before build, dist replacement, downloads or installation writes. The source path
is printed. Missing default prebuilt retains the existing incomplete developer-package
warning/README behavior and never silently falls back to local output.

Validation: complete shell packages with distinguishable committed/local fixtures
selected the expected file for default and opt-in. Missing opt-in failed before
mock-dotnet invocation and preserved an existing ZIP. PowerShell executed the actual
selection block for both paths; its complete missing-opt-in script failed before
toolchain checks or writes. With prebuilt absent and local output present, neither
default path selected the local file. Existing six uninstall, XML and Windows licence
staging fixtures passed again; Bash syntax, PowerShell parsing and diff checks passed.

An actual Release package was rebuilt with the stale main bundle temporarily visible
through a worker-only ignored symlink. Its bundle matched the committed SHA-256:
`cf9df05f092cf5f09eae559a32c11a5d948c13df1f81a24d10cc281b5a9b6c43`.
Build: 0 warnings/errors; package layout and text checks passed. The temporary symlink
was removed; neither actual asset was changed. Evidence is in
`/tmp/release100-bundle-check.py`, `/tmp/release100-bundle-check.log` and
`/tmp/release100-bundle-package.log`.
