# Build 521 — task-specific tutorial hands

The user clarified that both tracked sides must remain visible, not that both
controller models must remain visible throughout the first tutorial's VR lesson.
Build 518 incorrectly removed the established per-task hand/controller policy.

## Change

- Restore the original `ShowsController` table: card take/hold, fingertip interaction
  and prose cards use hands; button/stick teaching uses both controller models.
- Preserve the original reversible 0.22-second controller grow/shrink and hand crossover.
  Clear controller highlights on hand steps; retain the applicable-hand routing on key steps.
- Per-frame recovery uses the current task's policy, so it cannot reverse an intended
  transition back to hands. Both model animations continue ticking through a hand step.
- Preserve build 518's recursive VR render layers, missing-asset retry and replacement-hand
  rebinding. A newly identified missing-model path restores owned hand renderers before
  rebuilding or retrying. Stale model bindings are cleared while the requested key survives.
- No native gameplay callbacks, lesson admission, UI text, bundle or wire behavior changes.

## Validation scope

The user explicitly requested only affected checks, overriding the normal complete local
suite for this change. Run tutorial-controller and first-tutorial scope regressions, the
strict Release build, and narrow source/log/format checks. Do not run the umbrella guard
or full wire suite. Existing automatic dev CI policy is unchanged.

- Tutorial controllers: 3,452 runtime assertions, four source bindings and twelve
  negative controls pass. Coverage includes every original task for both dominant hands,
  continuous per-frame representation, unchanged key routing, reversing transitions,
  delayed assets, replaced tracked hands and destroyed controller models on all three
  recovery paths. The harness executes the production tick orchestration.
- First-tutorial scope: 42 runtime assertions, 17 bindings and eight negative controls pass.
- Strict Release build: zero warnings/errors. Default-level hardware diagnostics, shell
  syntax and whitespace checks pass. Source surfaces remain 625 config keys / 172 patch
  signatures / 4,731 log tokens, with no additions or removals.
- Independent worker review identified the missing-model hand restoration gap and reviewed
  the task policy. No full local guard or golden-wire run was repeated, as requested.

Hardware remains unverified; check both sides through card take/hold and fingertip tasks,
followed by a controller task. The existing automatic dev CI still runs on push.
