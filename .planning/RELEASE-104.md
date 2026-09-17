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
