# Release 1.1.0 readiness — Build671, 2026-10-11

The maintainer explicitly approved publication of 1.1.0. The bilingual player
highlights emphasize fully multiplayer-compatible immersive town NPCs and Steam
Frame compatibility/performance, including native standalone passthrough on
compatible OpenXR runtimes. Experimental PC-streaming passthrough remains in its
separate project and is not shipped here. The parallel Frame agent confirmed no
pending GloomhavenVR changes affecting this release.

The production difference from approved Build670 is the ModBuild increment to671.
The private compiled comparison has1255 types on both sides; all eight changed
types differ exclusively by inlined670-to671 handshake constants. There are no
new or removed compiled files. Version3/next-free118 remains the wire contract.
Defaults, assets, gameplay controllers and presentation implementation retain
the approved source. The release build's generated identity is version1.1.0 with
`IsDevBuild=false`.

## Local validation and its limits

- Source checks:16/16 pass. Public surfaces remain666 config keys,236 patch
  references and4795 log tokens; no removals. Documentation and bundle checks pass.
- Direct golden wire vectors:300510 assertions pass against the real game runtime.
- Strict release compilation:0 errors and0 warnings, with release stamping enabled.
- Private compiled baseline is Build670/dev829d3468. The original guard's snapshot
  function was copied unchanged into an ignored helper with only its private ROOT
  pinned, then invoked separately after focused checks. Script hashes are retained.
- The initial broad local invocation was stopped after the maintainer challenged
  duplicate portable testing. Its retained report records137 passes, one deadline
  failure and78 cancellations. This is not a complete216/216 pass. The source-check
  lines in its outer log are separate from those runtime counts.
- The first-picture failure rendered every required original at1.105787s, beyond
  its unchanged1s deadline. An isolated full positive rerun passes at0.790134s,
  including the strict checks after the deadline assertion. No implementation or
  deadline was changed. Contention is plausible but the stage stopwatches measure
  wall time, not CPU scheduling; the two observations do not prove a unique cause.
- Unchanged local scopes inherit the manifest-bound Build669 composed216/216 and
  Build670's eight focused suites. The new dev source must pass its own full hosted
  CI before main integration. Hosted CI includes98 registered portable suites;
  the real Unity runtime and full golden execution remain local evidence.

## Archive and CI audit

The local preflight archive was built from integration commitd66d43cd5 with
release stamping. Later integration documentation does not alter runtime source.
The archive is530159096 bytes, SHA256
`fd155778fdfa8e6e2401b067816505e8a086e767b35e4cbefb0bb337cd75b346`.
All102 file members pass ZIP integrity checks. All71 committed asset files match
their recorded SHA256 and sizes. Windows text encoding, full install layout,
Steam Frame launchers, configuration helpers and executable ZIP permissions pass.
The official release workflow rebuilds the actual reviewed main merge; its build
stamp and archive digest are recorded separately after publication.

[RELEASE-671-CI.md](RELEASE-671-CI.md) records the historical CI failure and its
already integrated repair. Current proof39/upload21/topology5/SDK9 checks and
release-history simulation36 pass. The live production proof verifier accepts
the latest Build670 full CI. No remaining workflow defect was demonstrated and
no healthy gate was weakened. Build671 still requires fresh exact-source proof.

Normal dev-to-main PR merging is permitted without a ruleset bypass. Publication
creates an immutable tag, verifies the uploaded archive while the release is a
draft, then publishes it. Afterwards the workflow records main release ancestry
and advances dev's next version through an ordinary fast-forward push.

## Evidence and cleanup

Ignored evidence lives under `.planning/debug/release671/`, the two recorded
central-runner reports, and their exact referenced native run directories.
The CI worker archive is hash-verified (`3cda7033efd27d7d55fcfa3cb34f3cbfe580e76a4f2f45ba36a4d7f010ba2f24`).
Completed and cancelled suite processes were reaped before owned cache cleanup.
Sources, reports, controls and the failed deadline attempt remain retained.
Additional redirected-project caches are cleaned only after recording their
actual source-project bytes and provenance; borrowed references and hardware
evidence remain outside this ownership scope. Current delivery artifacts are
retained, and private generated snapshots are archived before removal.

Automated rendering evidence is not a new headset observation. The maintainer's
release approval is the current shipping decision; previous hardware reports and
their verification boundaries remain documented in their original reviews.
