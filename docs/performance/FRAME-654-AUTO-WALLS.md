# Build 654 Auto wall eligibility repair

The supplied Build 653 Steam Frame run left Auto inactive after all rooms opened,
while manually selecting Hide all made the scenario substantially smoother. The
root log audit found cosmetic interaction preparation still marked active for
about 90 seconds after native room loading and reveal had finished. Build 652's
Auto predicate treated that background preparation as loading. It also vetoed
observation while VR Options were open and depended on Wine desktop-window focus.

The policy now defines loaded gameplay using the native controller's `IsLoading`
and `ScenarioIsLoading` flags plus actual `ScenarioRoomLoading.HasPendingReveal`.
Cosmetic card/figure preparation no longer blocks the FPS window or refresh of a
dirty hidden-wall inventory. Open VR Options no longer pause the FPS window.

Focus comes from the separately implemented cached nullable
`VRSession.InputFocus`, populated by actual OpenXR session callbacks. Explicit
false resets observation; null does not permanently veto a current, loaded VR
scenario. No native focus query, desktop-window poll, UI query, extra camera,
scene walk or timer was added to the wall policy. Its existing frame clock,
half-second loading grace, two-second observation window, 250ms per-frame cap,
editable 15 FPS default and per-scenario latch are unchanged. All instant wall
membership, ownership, door-frame/arch protection, wire recovery and renderer
consumer integration remain unchanged.

## Source and runtime evidence

The production source checkpoint is `937fb356a`. Worker strict builds also contain
the independent focus API checkpoint `0678a9003`, locally cherry-picked as
`fa831ee73`; the integrator must apply that dependency once only.

`scripts/check-wall-performance-runtime.py` runs the complete actual Performance
partial plus the already covered wall primitives in Unity 2021.3.5f1. The final
production case passes 695 assertions; all 22 causal controls fail at their
specified assertions and are therefore detected. Thirteen original controls are
retained. The obsolete requirement to exclude open options is replaced with nine
controls covering cosmetic preparation, open options, desktop focus, unknown XR
focus, explicit XR focus loss, native loading/reveal, the recovery grace and hidden
inventory refresh during cosmetic preparation. Compilation errors cannot pass a
negative control, and source stability is checked after execution.

The end-to-end policy replay holds cosmetic preparation true, VR Options open,
desktop focus false and XR focus true with native loading/reveal false. The first
room's 19 FPS frame intervals leave walls visible. Repeated 100–125ms frame intervals
then latch instant hiding. Separate cases prove each former veto independently;
genuine native loading, reveal or explicit XR focus loss prevent triggering and
discard prior slow observations, followed by a fresh grace and full window.
Unknown XR focus permits observation. Improved FPS, option opening after hiding,
manual Regular and new scenario identity retain the existing latch behavior.

This fixture executes real native renderer flags, hierarchy, MPBs, scene handles
and camera pixels. Original native controllers, cosmetic preparation, options,
focus and clock are explicit deterministic boundaries. Collector, broad
classification and attachment restorers retain the previously documented model
boundaries. The new focus writer's actual callback/lifecycle proof belongs to its
separate scope. The fixture does not establish headset FPS, complete original-game
or DLC execution, or a connected multiplayer result.

The existing warmed entry timing is against the identical entry with the wall
policy guard removed and a cheap Tick boundary. Across 15 interleaved AB/BA rounds,
median added Auto entry time is 0.00032245ms. This bounds the policy entry in the
local fixture; it is not saved whole-wall CPU time or a measured Frame FPS gain.

## Worker validation receipts

- Complete wall runtime: `.planning/debug/frame654/all-controls/run-927tq_fp/`,
 695 production assertions, 22 causal controls, unchanged captured inputs.
- Initial production-only runtime: `.planning/debug/frame654/production/run-bjyeckpn/`.
- All 16 source scopes: `.planning/debug/frame654/source/results.json`, pass.
- Strict Debug and Release: `.planning/debug/frame654-build-debug.log` and
 `.planning/debug/frame654-build-release.log`, zero errors and zero warnings.
- `git diff --check`: pass.

These are focused worker checks. The primary agent records integration checks and
hardware analysis separately; they are not a fresh complete local-gate run. Actual
automatic activation, retained protected geometry and perceived smoothness remain
next-headset-test acceptance.
