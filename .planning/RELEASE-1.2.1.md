# Release 1.2.1 readiness

The maintainer authorized publishing the current dev frontier as1.2.1. The
implementation frontier is f3a2d9271, Build720. Its local bounded checks and
hardware limits remain recorded in the per-build notes. The semantic version
already names1.2.1 after the prior release bookkeeping; Build721 assigns the
release its own multiplayer handshake identity. No gameplay, presentation,
default, asset or wire implementation is changed after720; wire3/next-free123.

## Highlights

English-only player highlights describe physical active-card/summon dismissal,
original local/remote flights, automatic item-fan closure, shared fixed window
geometry/distance, quest-window convergence, latejoin placement recovery and
item-surrender cleanup. No new GPU-performance or hardware-acceptance claim is
made. Installation/player documentation remains bilingual.

## Hosted blockers and verification

Previous dev719 CI39863889124 passed source/build and three runtime shards, but
shard3 failed three fixtures. The flight-timing fixture extracted the current
production TransferVisibleFlight without ports for its new dismissal-source context.
The active-dismiss and selection-latejoin launchers hardcoded ~/.dotnet/dotnet,
which is absent on the hosted runner. The current720 CI was still running when
release preparation began; it is not treated as passing evidence.

The SDK repair preserves an explicit DOTNET executable, then uses PATH and the
existing local fallback; it stops replacing the host's DOTNET_ROOT. Actual runner
launch probes reproduce seven before-repair failures and pass all14 SDK tests
with empty runner homes, installed/explicit SDKs and missing SDK fallback.
Fixture repairs retain the production methods, behavior assertions and causal
controls. Neither a compile failure nor a negative control is counted as a
passing game-behavior run.

The SDK fixtures pass145/6 and47/6 assertions/causal controls; the repaired
flight fixture passes841/16, retaining all13 prior causal controls and adding23
assertions/3 controls. The initial worker compile exposed a System.Numerics
lowercase-member adapter omission; its corrected full rerun passes. All16 source
suites, strict release compilation with zero warnings/errors and all306431
unfiltered real-runtime golden assertions pass. The private compiled comparison
finds nine build-identity-only types and two intended release-build consumers
(BuildInfo/SelfUpdateConfig: IsDevBuild=false), with no other behavior difference.
All669 settings,242 patches and4816 log markers remain; bundles and bilingual
player docs pass. The rendered release text is English-only. Exact receipts remain in `.planning/debug/release121/`.
Unchanged passing Build720 implementation evidence is reused; there is no second
complete local runtime sweep merely for a release identity or fixture repair.
The candidate requires its own successful full source-tree hosted CI before the
reviewed dev→main merge. Publication rebuilds/packages the exact main commit and
automatically records its ancestry and next version on dev. No manual tag or
publication bypass is authorized by this preparation.

Completed local runs use canonical cache cleanup; worker evidence and release
outputs are retained, and only completed owned worktrees may be removed after
byte-compared archival. Other agents' worktrees, hardware logs and game references
remain untouched. No local duplicate full movie archive is generated; the
release workflow validates and uploads the complete package.

## Publication completed

Full dev CI39867327604 succeeds: all110 hosted runtime suites, source/build
checks and the final full-check admission pass. PR21 CI39869661955 reuses that
exact source proof and passes its main-base surface comparison. The normal merge
creates main7b173b376f2ab45c997a109cd67cfd6b3a10b5ff, whose tree exactly matches
the reviewed devf46f368a63eb1d3e46644b48f99f50c4c35bf4d8.

Release39870344161 completes successfully, including fresh release build,
package validation, immutable tag, verified upload, publication and dev
bookkeeping. Tagv1.2.1 resolves to that main merge. The public latest stable
release contains GloomhavenVR-1.2.1.zip,1,421,389,293 bytes, SHA256
`cbc177972b22aa480b00482817c090176ca63aa46102b6e5c08fc8d7f9800a85`.
Anonymous download HEAD returns200 with the exact archive size. The published
body contains the exact English highlights. Dev is advanced to1.2.2 and retains
the release ancestry; no tag was moved and no publication bypass was used.

API receipts, complete hosted dev/release logs and anonymous-download
verification are preserved in `.planning/debug/release121/`. The tester-default
check found zero cfg entries and is not claimed as a hardware-profile comparison;
Git confirms no Defaults/prebuilt/movie-manifest changes sincev1.2.0.
No additional headset test is inferred from publication success.

Release: https://github.com/McFredward/GloomhavenVR/releases/tag/v1.2.1
