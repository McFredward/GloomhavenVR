# Quest754 integration of release1.2.3

The maintainer explicitly requested "Rebase auf1.2.3" on2026-10-31. This
approval applies to the published tag `v1.2.3`, exact commit
`b87477f8ae6247e524efc6dc128c09a25a6ae55d` (upstream Build753), not later dev
work. Keep the longstanding Quest branch exception: publish only
`feature/quest3-standalone`; do not alter dev/main. Future rebases still require
another explicit maintainer go after their review.

The prior published Quest743 tip is
`7ecf6aaeae253b7d952ab4fcb7e80b78717519be`, based on dev742
`fc74ee79c75878ea097cf2dce02da94f3670275b`. Its exact tree was consolidated on
that original base and actually rebased onto the release tag. An ancestry-only
merge retains the published Quest tip, allowing a normal non-force feature push.
The new Quest ModBuild754 is reserved with the dev integrator; package version
remains1.2.3. This is not a new upstream release or a dev version bump.

## Scope and validation plan

Class C: upstream744–753 changes shared native menu/capture/input/window and
character creation lifecycle. Preserve exact current upstream behavior, native
state/authority and fixed-quality presentation together with existing centrally
gated Android adapters. Retain the naming keyboard clearance and personal-quest
preview framing from753. Test-fixture conflicts combine both source/negative
control rosters; the upstream retained-original byte-equality checks supersede
older weaker timing assumptions. No suite or control is deliberately removed.

Run the complete integrated local guard once, including source gates and actual
wire golden executable, strict Debug/Release, metadata/reference and exact
Android-resource-bound/AOT checks. Independent workers qualify the Android
reader closure, final minimal update plan and source parity. After any bounded
repair, repeat its affected containing checks and retain input-bound unchanged
coverage; do not call aggregate coverage a new successful full invocation.
Record actual commands, counts, elapsed time and failures in final evidence.

The user builds/updates on Windows. Do not produce a local APK or delivery ZIP.
Prefer retained qualified resource pairs and unchanged original game/art/audio/
movie/Story data; no full shader census or Campaign regeneration merely for a
source update. Unknown producer/ABI drift must remain explicitly rejected.

Evidence: `/home/claw/quest3-local/build/evidence/quest-v1.2.3-rebase-20261031/`.
Actual headset appearance, startup, controller alignment, frame pacing and
multiplayer acceptance remain separate hardware checks. Preserve hardware inputs,
current artifacts, original references and active work from other agents; clean
owned regenerable caches and retire workers after the run.
