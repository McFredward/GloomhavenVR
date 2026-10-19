# Release 1.2.0 readiness

The maintainer explicitly requested publishing the current `dev` development
frontier as 1.2.0, highlighting native Steam Link passthrough and stereoscopic
videos. The follow-up explicitly requires an English-only release body. The
versioned highlights and generated common release prose are English; bilingual
installation/player documentation remains bilingual.

## Candidate scope

The implementation frontier is `ebb6116d5`, ModBuild 714. Its full source/build
and four-shard runtime CI 39314776414 completed successfully. The release changes
only `<Version>` 1.1.1→1.2.0, the required distributed-build handshake 714→715 and
its explanatory comment, release highlights and development records. Gameplay,
presentation, defaults, asset bytes and wire implementations are unchanged from
the reviewed frontier. Version 3/next-free 118 remains the network contract.

The new release's own full exact-source dev CI is required before merging its
dev→main PR. The previous frontier's success is inherited implementation
evidence, not a substitute for that candidate proof. No full 231-suite local
gameplay umbrella is repeated for these release identity changes. Relevant
local source, direct real-runtime golden, strict release and compiled-scope
evidence is recorded under `.planning/debug/release120/`.

Initial local source checks pass16/16 and strict release compilation passes with
zero errors/warnings and `IsDevBuild=false`. Direct golden vectors pass300601
assertions. The first direct execution exposed a missing local SDK environment;
the corrected invocation then found one inherited stale source-format assertion
(300597 passed/one failed). RayGrab's unchanged carry stamp had gained a braced
trigger-claim block in711. The bounded test repair preserves that assertion,
accepts both exact block forms and adds three assertions for live tick order and
the removed-stamp/reversed-order controls. No production behavior is changed.
All failed attempts and their cleanup receipts remain retained alongside the
passing `run-lmbs4gqo/` and source-final report. Private compiled comparison and
hosted proof are completion requirements before main publication; their final
records remain in the ignored evidence so recording results does not alter the
candidate being validated.

## Highlight and asset audit

The English highlights lead with native Steam Link passthrough, followed by
the 3D video library. They link the separately installed paired PC/Frame
companion, state its current Windows/NVIDIA boundary, distinguish built-in
standalone passthrough and preserve the documented interruptions limitation.
They do not claim universal headset/GPU support, perfect hardware continuity,
six-degree-of-freedom movies or that every original game video is stereoscopic.

`packaging/movie-files.txt` requires 22 separate movie files: 20 stereoscopic
movies and two mono fallbacks. All inputs exist. The 20 stereoscopic movies
cover the mod intro, 11 character films,seven ambient films and the studio logo;
all report 3840×1080 metadata. Movie names/test actions are not exposed in the
release highlights. Deliberate test playback remains behind Advanced>Cheats and
the existing default-off permission, independently of normal native playback.

The independent native companion's public archive was freshly streamed and
verified against its actual bootstrap pins: 18,313,310 bytes, SHA256
`782502481fb766d276fbab9ef211d1dbff7005b93236041077189ad74b21a40b`, with
PC/Frame 0.2.11 manifests. That archive is distributed through its own public
repository, not the GloomhavenVR ZIP. A stale local 0.2.7 receipt is explicitly
not accepted as current proof. The separate project's README/COMPATIBILITY
documents remain authoritative for its installation and runtime requirements.

## Publication and provenance

Use a normal reviewed dev→main PR with a merge commit. The release workflow
requires the exact source tree's trusted full dev CI before rebuilding with
`GhvrReleaseBuild=true`, validating its actual complete archive, creating the
immutable tag v1.2.0, verifying the draft upload and publishing. Do not create a
tag manually, publish a dev DLL artifact or overwrite an existing release/tag.
After publication the workflow retains release ancestry and advances dev's
semantic version to 1.2.1 through a normal push. ModBuild is independent.

No device was modified or restarted for release preparation. Software tests,
asset probes and public download verification do not establish a new headset
picture. Publication is the maintainer's explicit shipping decision; historical
hardware evidence and remaining verification limits stay in their original
development records rather than being silently promoted to measured outcomes.

Own worker evidence and generated release/source/golden/compiled verification
are retained below `.planning/debug/release120/`. Completed/reaped fixture
processes use canonical cleanup; logs, original source, reports, hashes, hardware
inputs and current deliverables are preserved. Other agents' worktrees and
shared game references are outside cleanup scope.
