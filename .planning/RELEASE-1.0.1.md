# Release 1.0.1 — build 509

## Scope and authorization

The maintainer requested combat log startup disabled by default followed by release
1.0.1 on 2026-09-15. The existing Version is already 1.0.1. Build 509 changes the
annotated WorldUI/CombatLog startup preference to false. Existing saved preferences
still win through the normal ConfigFile binding; players can show the log at any
point with the existing VR options action.

This release also includes builds 505–508: native/shared video windows, native video
skip, savegame tutorial continuation, original hint geometry and stable party window
placement. Bilingual player highlights are in packaging/release-highlights/1.0.1.md.
The user confirmed the prior tutorial deadlock resolved. Automated checks do not
replace headset confirmation or the outstanding four-player performance test.

## Release preparation findings

GitHub CI run 35011688383 for build 508 failed because the hosted Ubuntu runner had
no rg binary. The map-button negative control detected its intended assertion, but
the shell could not verify it. Later native-video/hint harnesses also require rg.
This was not reproduced by local tests, where ripgrep is installed. Both workflows
now explicitly install the required runner dependency.

The live GitHub main ruleset requires pull requests, allows merge commits and has
no bypass actors or required approving reviews. The previous release workflow only
accepted main commits already contained in dev, rejecting a fresh PR merge commit.
The corrected provenance check permits the protected branch merge while verifying
that its tree is exactly the tested dev parent and that the parent remains in dev.
The release remains built/tagged from main. Post-release bookkeeping carries the
release merge ancestry back to dev before advancing its version; it preserves
concurrent dev changes and uses ordinary fast-forward pushes.

Repository visibility is already public. v1.0.1 did not exist at preparation time.
No license or visibility changes are part of this request.

## Validation and publication

Local validation passes all 17 source checkers, 253,674 wire assertions and the
production regression suites with their negative controls. Strict Release has zero
warnings/errors; bilingual documentation and whitespace checks pass. The retained
build-502 compiled comparison has 32 changed types, 14 additions and no removals.
Relative to build 508, the two additional changed types are Defaults and WorldUIConfig:
the compiled ConfigFile binding now supplies false for the startup preference.
Surfaces remain 625 config keys / 161 patch signatures / 4,724 log tokens.
The default is pinned against future cfg rebases. The retained tester cfg still differs
in Cheats.Enabled, Comfort.VerticalDrag and General.LogLevel; those stale/test-specific
values were reviewed and were not imported as release defaults.

The integrated production release helper passes 36 offline Git assertions, including
three successive PR releases, invalid same-tree/squash/resolution commits, concurrent
dev changes, rejected-push retry and independently advanced versions. The historical
main-bump failure reproduces with --old. Workflow actionlint, Bash syntax and YAML
validation pass.

Publication completed on 2026-09-15 at 19:39 UTC:

- Final candidate CI: [35013539643](https://github.com/McFredward/GloomhavenVR/actions/runs/35013539643), successful for dev 6dc66256.
- The maintainer merged PR #3 using a merge commit after the API token refused PR
  creation. main and annotated tag v1.0.1 both name
  be74759e33f87aa76be313071010f1ddcbecdf47.
- [Release workflow 35014316673](https://github.com/McFredward/GloomhavenVR/actions/runs/35014316673)
  completed successfully, including publication and dev bookkeeping.
- [GloomhavenVR 1.0.1](https://github.com/McFredward/GloomhavenVR/releases/tag/v1.0.1)
  is public, latest, and neither draft nor prerelease.
- The anonymously downloaded GloomhavenVR-1.0.1.zip is 85,069,252 bytes, passes ZIP
  CRC validation, and matches GitHub's SHA256:
  4a9c084281f763a1f1e55bc4e9c1561642abbb91bc3ede990e88c0ce39e59af6.
- The ZIP contains the 74,943,763-byte asset bundle. The actual downloaded plugin's
  compiled metadata identifies Version 1.0.1, ModBuild 509, commit be74759 and
  IsDevBuild=false. This verifies download provenance, not headset behavior.
- Bot merge d6062e55 records main ancestry on dev; bot commit 12f66604 advances
  dev to Version 1.0.2. The only tree difference between main and that dev tip is
  the csproj version. The local dev checkout was fast-forwarded to it.

The public latest-release endpoint returns v1.0.1 and the expected ZIP. This does
not by itself verify an update prompt or installation in a running headset.
