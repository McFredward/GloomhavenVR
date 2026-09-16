# First-tutorial-only VR additions — build 514

## Report and evidence

The maintainer reported that every tutorial received the additional VR lessons; only the first
should receive them. Later tutorials retain native progression, with VR wording adaptations where
needed. This is a source-proven admission defect: the scenario-start postfix called
`ControlsTutorial.RequestForTutorial` for every `TutorialVR.IsTutorialActive` context. The extra
figure-grab step also accepted its matching hint key in any tutorial.

The retained hardware logs still identify local build 513 / commit 66df2561c and historical remote
build 500. They are not a new build-514 tutorial capture. No screenshot accompanies this report.

## Implementation

`UITutorialSelectorWindow.CreateTutorialOptions` displays `TutorialService.GetTutorials()` in
list order. The first descriptor is therefore the game's authoritative first tutorial, regardless
of its internal filename. A read-only prefix on `TutorialService.StartTutorial` copies that first
entry's nonempty ID and filename before the menu service is unloaded. The native loader yields
before loading the scenario; native current tutorial identity is assigned before scenario startup.
The prefix never suppresses or changes the native load. Lookup failure clears previous admission.

`TutorialLessonScope` requires both exact identities, FrontEndTutorial mode, offline play, an
active native event controller and the existing tutorial error latch. It is separate from the
broad input/text compatibility predicate. Replaying the first tutorial retains its VR lessons;
there is no saved completion flag or guessed Tutorial_1 filename. Unknown identity inserts no
additional steps. Native wording/input adaptation remains available in later tutorials.

Controls admission, opening-dialog dismissal, running/clicked controls steps, the extra figure
step, camera-page skipping and message holds all use this scope. Loss of scope retires pending
work and releases still-live native messages. Every scripted-level boundary resets old lesson,
figure-step, camera-skip and card-name state before checking the next tutorial's eligibility.
A hold records its native controller when engaged and cannot capture or replay messages belonging
to another controller. Native outcome messages remain exempt from holds.

## Verification

The focused harness executes production scope, its Harmony prefix and the complete production
message-hold class with controlled native APIs. It covers unknown/malformed catalogues, first and
later tutorials, inconsistent identities, menu unload, replay, other game modes, online/inactive
contexts, lookup failure, FIFO native continuation, scope loss and controller replacement.
Source bindings connect that policy to lesson admission/cleanup and preserve generic adapters.

- 42 runtime assertions and 17 production-binding assertions pass.
- Seven runtime negative controls reject identity/file/mode/online admission regressions and
  hold-admission/capture/replay regressions. One binding negative control rejects a gate retained
  only as a comment.
- The harness is registered in local validation and both existing hosted test suites.
- Full integration validation is recorded below after completion.

Hardware acceptance remains open: first tutorial -> later tutorial -> first tutorial again,
including leaving during an additional lesson. The tests establish source behavior and bounded
native continuation, not headset appearance. This change targets dev version 1.0.3; published
1.0.2 remains unchanged.
