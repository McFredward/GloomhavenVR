# Release 1.0.5

Published: version 1.0.5, ModBuild 534, hardware-tested source at 82d7ac08.
The maintainer confirms the Guildmaster table and split controls work. The short-rest
burn flash persists and is explicitly deferred for this release. Investigate that issue
only after successful publication, preserving the released source and tag.

## Release assessment

Current local logs are build 534 / assembly 1.0.5.0, captured 2026-09-18 12:19.
Peer logs remain historical build 500. No new screenshots accompany this report.
There is no recorded gameplay exception or deadlock. The thirteen mod Error entries are
bounded MaterialLoaderHeal failures for one coin material (asset
2c309731defe50f4d84721fd7f50c5c4). The preceding renderer census associates these coin
meshes with decorative Treasure_Bay_Sm_01 scenery; they are not evidence of missing
collectible loot or blocked native progression. Retain this as an unresolved decorative
asset issue rather than claiming an error-free log. Independent audit is recorded separately.
Native Hydra DNS requests fail; shutdown NullReferenceExceptions occur after OpenXR EXITING,
including VoiceChat.BoltVoiceChatService.OnDestroy. Neither establishes a gameplay deadlock.

Bilingual release highlights describe the shipped visible changes and explicitly retain
the short-rest visual issue. No additional gameplay code or ModBuild bump is included.

## Validation and publication

The candidate's unchanged production code passed the full local build-534 source/runtime
guard and 254,565 wire assertions, strict Release with zero warnings/errors, bundle checks,
Actionlint and bilingual documentation checks. Focused totals: burn replay 672 / seven
bindings / 32 negative controls; Guildmaster geometry 10,338 / 25 / nine; MR 79 / 26 / six.
Only release documentation was added for publication. Fresh hosted exact-tree CI passed
before merging dev into main. Main builds/packages the actual merge without repeating the
full development suite. Keep both branch history and release tags immutable.

The optional defaults comparison used the historical tester snapshot. Its three differing
values are deliberate: player Info logging, cheats disabled and the user-requested vertical
stick movement enabled. Three nonliteral card defaults resolve to the exact snapshot values
(30 degrees, zero offset, .22 seconds). Legacy unbound settings and migration markers are
not copied into player defaults; no defaults changed for this release.

Publication completed on 2026-09-18:

- Development CI 35334463770 passed for candidate 1409af0f; PR CI 35334503796
  reused that identical-tree validation successfully.
- PR #10 merged normally into main at 377d26ec4f28b8bd5f40a8f364365e23372e21cb.
  Its Git tree equals the tested candidate tree, 62fd1160a4dc8a05079a96cba5b60668f9773114.
- Release workflow 35336124536 succeeded, including packaging, upload and dev bookkeeping.
  Tag v1.0.5 points to that exact main merge. GitHub release 391397604 is public,
  non-draft, non-prerelease and Latest:
  https://github.com/McFredward/GloomhavenVR/releases/tag/v1.0.5
- GloomhavenVR-1.0.5.zip (asset 572418159) is 85,171,359 bytes. An independent download
  passed ZIP CRC validation; plugin, preloader and bundle are present. SHA256 matches
  GitHub's digest: e3a7979d08d599d2bfc8125bcd70c4e2c50165495c6b38b02cabb938142f023e.
- The workflow merged release ancestry into dev and advanced its package version to 1.0.6
  at 9081a992. The integration checkout was fast-forwarded to that commit.

Short-rest investigation resumed only after successful publication. Follow-up changes belong
on dev; the 1.0.5 release, tag and main source remain unchanged.
