# Release 1.0.4

**Published 2026-09-17.** Main Release run
[35276519563](https://github.com/McFredward/GloomhavenVR/actions/runs/35276519563)
completed successfully, including upload and post-publication dev bookkeeping. Latest is
[v1.0.4](https://github.com/McFredward/GloomhavenVR/releases/tag/v1.0.4).

The immutable annotated tag remains fe80b45f, pointing to main commit a6d044c4. The main
runner rebuilt that exact source with release settings and reused successful full dev CI
35269595138; no full test suite ran again on main. Build and packaging reported zero
warnings/errors. Uploaded archive: `GloomhavenVR-1.0.4.zip`, 85,152,284 bytes,
SHA256 `b7f45b5b8822d0633dfc3c5043825685cc270549d9efc21e78b9a019a2fffb3d`.
Release ID 391041682 is public, contains the verified archive, and is Latest. The local
recovery ZIP was not used for publication. Dev is now 1.0.5 at 24c8fce9, ModBuild 527.

Pipeline recovery landed through PRs #8 and #9. Full dev runs 35272595156 and 35274984310
passed; PR runs 35273927614 and 35276234468 reused evidence and skipped full checks.
The final uploader passed 21 focused tests, including seven HTTP-adapter cases and a real
read-only draft-discovery/provenance check. The earlier failures below are historical.

Candidate: version 1.0.4, ModBuild 527, based on the hardware-tested dev source at
13d6f69b. The maintainer reports the latest hardware round looked correct and authorizes
publication after log review. This release must be a reviewed dev-to-main merge; the main
workflow builds and publishes the exact main commit with existing full dev CI evidence.

## Hardware evidence

The local test logs identify build 527. The files under debug/remote remain build 500;
they are not evidence for current multiplayer behavior. No new screenshots accompany this
successful hardware report.

The current mod log has no Error/Fatal entries. The encounter opening animates one older
window (LogOutput:480), explicitly keeps the incoming window fixed, and completes (492).
The quest popup's no-readable-solution report (363) leaves the incoming pose and native UI
untouched. No return of the earlier incoming-window animation is recorded.

DisplayRect warnings at LogOutput:1177–1185 occur during an explicitly requested round
restart, after EndScenarioSafely, followed by scene initialization. There is no exception
at this transition. Actual NullReferenceExceptions in Player.log occur after application
quit/OpenXR teardown, with native VoiceChat and ObjectPool/Chronos cleanup stacks. These
are retained as shutdown diagnostics, not claimed absent or proof of a gameplay deadlock.

The source tests and hardware report support releasing the current fixes. They do not
establish every headset configuration or replace a fresh matching-build multiplayer log.

## Publication checklist

- Bilingual player highlights prepared in packaging/release-highlights/1.0.4.md.
- Review final full dev CI and local release gates, including real-runtime wire vectors.
- Merge dev into main with a merge commit; do not create a dev release or manually move tags.
- Follow Release through ZIP upload and next-dev-version bookkeeping.
- Verify v1.0.4 targets the main merge and the expected archive is attached.

## Publication status — upload blocked

PR #7 merged as a6d044c4b0f684556bdb5f240f15978a2f554aa7. Its tree matches the final dev
candidate exactly. Full dev CI 35269595138 and reused PR CI 35270754550 succeeded. Local
release gates passed, including 254,565 wire assertions, strict Release with zero warnings
and errors, and bundle validation. The compiled comparison against the older 080c505e
baseline reports the intended intervening development changes (86 types changed, 63
added/removed, one order-only move); no config, patch or log surface was removed.

Release workflow 35270985542 built and validated the package, pushed v1.0.4 to the main
merge, then failed at asset upload with HTTP 500: Error saving asset. No release was
published. The tag is valid and was not changed. A normal workflow rerun would reject that
existing tag; do not delete or move it to work around publication.

Recovery rebuilt the exact clean main commit in /tmp/gvr-release104-publish using the
committed metadata references, pinned RuntimeDeps and verified OpenXR natives, with
GhvrReleaseBuild=true. BuildInfo identifies a6d044c4b and IsDevBuild=false. The package
layout/text/bundle checks pass. Durable local archive:

    dist/recovery-1.0.4/GloomhavenVR-1.0.4.zip
    SHA256 d8f8f383137d3ff8406d14f0670c0b0b456137d36e660ba48cce465e598f4555

Draft release 391041682 now holds the prepared notes for v1.0.4 and no assets. Both the
separate CLI upload and direct REST upload failed with HTTP 500: Error creating asset temp
dir. The final request ID was 970A:CC3F6:14CB701:16BF1E4:6AAC4EF4. GitHub's public status
endpoint reported operational, which does not negate these actual endpoint failures.
Latest public release remains v1.0.3. Dev has deliberately not been bumped before publication.

The maintainer subsequently requested recovery through the main build pipeline, keeping
full tests on dev. The local recovery archive is therefore not the publication source.
A main-only manual resume path is being integrated: it will verify the original tagged main
source against existing full CI proof, rebuild and package it on the main runner, preserve
the tag, retry draft uploads and verify the uploaded archive before publication. Normal
post-publication dev bookkeeping follows. No game code or ModBuild change is involved.

The recovery workflow landed in main through PR #8 (e9cb378d). Full dev CI 35272595156
passed; PR CI 35273927614 reused its proof and skipped full checks. Main recovery run
35274136888 accepted the original tagged tree's proof and successfully built/packaged it.
Publication exposed an adapter defect: GitHub's release-by-tag endpoint does not discover
drafts. Five newly created empty duplicate drafts were removed after checking each ID,
tag, draft flag and empty asset list; the original draft 391041682 remains. Recovery is
being corrected to discover authenticated draft listings and pin the release ID for all
subsequent requests, including upload. API-adapter tests and a real read-only discovery
check are required before the next main attempt. The corrected adapter passes 21 upload
tests, including seven HTTP-adapter cases. A real read-only call successfully discovered
and pinned draft 391041682 and verified a6d044c4 against the tag and main history.
