# Release 1.0.4 candidate

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

Resume by uploading the verified archive to the existing v1.0.4 draft, verify server size
and digest, then publish with `gh release edit v1.0.4 --draft=false --latest`. Only after it
is live, perform the normal release-provenance.sh prepare bookkeeping from fresh refs and
push the resulting next-version commit to dev. No new game code or main merge is needed.
