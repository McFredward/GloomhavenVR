# Optional board explanations (759)

Class B: bounded presentation/configuration change on dev 074f42adb (Build758).
The control board's optional explanatory text has three owner preferences: Always,
Never, and After a delay. New PC and standalone profiles both default to a
150-second delay (maintainer follow-up: 2.5 minutes). Cards & Hand exposes the mode and, only for delayed mode, its
0–3600-second interval. Existing profiles receive these new defaults. EN/DE names,
mode labels and player-facing help are provided.

## Behavior and authority

`DecisionHelpClock` observes an actual native decision or one of the original
three automatic synchronization barriers. A native wait/actor/phase/prompt change
or accepted card response (including replacement or reordering with unchanged
count) restarts the timer. Head movement, fan motion and temporarily hiding the
board do not. Settings apply on the next existing board update. A choice-free
state never acquires an instruction. The clock has no native input, rule-state,
acknowledgment or advancement writes.

This setting controls only the mod-authored fallback explanation. Native required
card/item/panel choices, exhaustion facts, element/bonus instructions, controls and
important alerts retain their existing priority. The optional writer checks the
actual resident banner before changing or clearing its own text. Independent
20-second bounded automatic-wait diagnostics retain their original threshold and
normal log importance; only their optional displayed explanation is delayed.

The existing owner pick-banner sampler (record7) sends visible text or absence.
RemotePickBanner applies that result without a viewer preference or second timer.
There are no wire-format, protocol-version, remote-card, native-game or unmodded
client behavior changes. Wire exemptions explicitly document these derived owner
results.

## Qualified verification

- Final strict Release build: 0 warnings, 0 errors, 10.30 seconds.
- Final complete source group: 16/16 passing, 32.8 seconds. This is the full source
  group, not a full local runtime gate.
- `card-progress-lifetime`: 462 assertions and 23 compiling negative controls,
  82.58 seconds. Actual help consumer, clock, native cancellation owners, original
  cancellation body, default declarations, locale mode labels, owner sampler and
  RemotePickBanner.Apply are source-bound. Covers 299/300-second boundary,
  live mode/delay changes, local meaningful selection/replacement, wait identity,
  no timer reset on view changes, native banner precedence, owner/remote hiding,
  divergent viewer settings, malformed intervals and clock rewind.
- `event-dispatch`: 62 assertions and 5 compiling negative controls, 22.13 seconds.
  Source-bound actual event owners include the pure immediate wait accessor and
  unchanged diagnostics. Covers all three automatic states, readiness/online
  changes, required-choice exclusion, VR shutdown and side-effect-free access.
- Existing `vr-options` and `vr-options-close`: 2/2 passed, 147.89 seconds;
  7,339 options assertions, 915 video-review assertions, 1,800 close assertions.
  Their scope is existing common entry/callback/closure machinery and spoiler
  gates; new settings registration, dependency, localization and wire exemptions
  are checked by the source group, while their values are exercised by the
  help runtime. These fixtures were reused unchanged after the final bounded
  help/native-banner precedence fix, which cannot reach their tested code.
- `git diff --check` passed.

Initial source checks correctly rejected missing derived wire exemptions and the
unpublished Decision-prefixed keys joining the existing board geometry family.
Keys were renamed ProgressHelpMode/ProgressHelpDelaySeconds before publication;
no unrelated geometry setting was moved. A mistyped options runner invocation
omitted --group and executed no suite; the corrected invocation passed. The
final precedence test initially mixed an unrelated mandatory banner's new wait
with the same clock-age assertion; it was isolated into a second actual help
consumer, and the complete containing suite/controls passed afterwards. All
failed logs and source snapshots are retained. No test failure is suppressed.

The owner-sampler and remote Apply bodies execute with explicit lightweight
Unity/TMP/native API ports; these establish visibility provenance, not Photon
latency or final headset pixels. The option does not change renderer geometry.
No hardware outcome or complete local/golden gate is claimed. The integrator
qualifies the final union against the current Build758 evidence.

## Evidence and cleanup

Evidence is retained in main .planning/debug/decision759/worker. Source-bound
fixtures preserve input hashes, executed assemblies, negative-control logs and
cleanup receipts. Every standalone fixture invoked cleanup_completed_run after
its process completed, including failed runs. Production build caches were
removed after copying only the current plugin/preloader deliverables and their
source-input hash manifest. Shared resources and other workers were untouched.
