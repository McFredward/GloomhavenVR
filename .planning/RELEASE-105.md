# Release 1.0.5

Candidate: version 1.0.5, ModBuild 534, hardware-tested source at 82d7ac08.
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
Only release documentation is added for publication; fresh hosted exact-tree CI is required
before merging dev into main. Main builds/packages the actual merge without repeating the
full development suite. Keep both branch history and release tags immutable.

The optional defaults comparison used the historical tester snapshot. Its three differing
values are deliberate: player Info logging, cheats disabled and the user-requested vertical
stick movement enabled. Three nonliteral card defaults resolve to the exact snapshot values
(30 degrees, zero offset, .22 seconds). Legacy unbound settings and migration markers are
not copied into player defaults; no defaults changed for this release.

Publication pending: push candidate, verify hosted CI, create/review/merge dev -> main PR,
follow Release through uploaded ZIP and dev bookkeeping, verify tag/asset/latest metadata.
