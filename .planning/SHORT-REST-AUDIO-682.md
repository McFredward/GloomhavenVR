# Build682 — fan audio during a deferred burn layout

Root reserves ModBuild682 on current dev6455c3f21 (runtime681) for the maintainer's
October17 short-rest noise report. This is a bounded repair; it does not require
repeating the unchanged complete local or golden-vector gate.

The supplied local LogOutput.log/Player.log both identify680. Its single original
Frost Armor burn runs from frame80907 through80998 (2.013s). Within that interval,
eight throttled FAN SOUND close lines request PlaySound_UICardTabSelect; two
logged attempts succeeded. The log interval is0.25s, so this is not the total
underlying audio count. Remote Player.log is old October11 vanilla evidence.

Source cause: CardFan.Close intentionally returns while BurnLayoutPending, keeping
IsOpen=true and preserving native burn geometry. UpdatePalmGate nevertheless
emits a close sound and retries the same branch next frame. CardFan.Open has the
same refusal contract. Audio must follow actual open/closed transitions, without
a debounce, new timer or change to the burn barrier.

Disjoint ownership:

- Initialized worker /home/claw/gloomhaven_vr_fan682_audio, fromdev6455c3f21:
  CardsDriver.2.Update.cs, existing BurnLayoutTests fixture and its runner only.
  Implement transition-sensitive audio and regression controls; commit, never push.
- Root: independent source review, NetProtocol build note/version, STATE, bounded
  final validation, integration and direct dev push. Preserve unrelated Steam Link
  receiver and trailer work; no changes to their sources or evidence.

Use the shared completed-run cleanup API after reaping fixture processes. Preserve
hardware excerpts, source hashes, generated source and failing control evidence.
Archive and verify worker evidence before removing only this completed worktree.
Headset sound attribution remains unrecorded; the repeated audio call defect is
established by paired source/log timing. Final validation receipts will distinguish
this scope from actual headset playback and from inherited unchanged coverage.

## Final repair and focused evidence

The shipped method gains only two post-call state checks and a causal comment.
Its existing opposite pre-state guards establish actual transitions; no new latch
or timing rule is introduced. CardFan, the burn barrier, native game callbacks,
empty-fan feedback and audio helper/configuration/suppression remain unchanged.

The extended existing suite executes the exact driver branch and complete actual
CardFan.Open/Close methods with named hierarchy, animation-helper and audio sinks.
Burn-wait and desired visibility are explicit inputs; no full palm measurement,
audio-device playback, native gameplay or headset proof is claimed. Its unchanged
228 burn assertions/16 controls and new737 audio assertions/two controls pass.
Both animated and instant close,180-frame refused open/close bursts, release,
stable repeated frames, later normal edges and canceled intent are covered.
Restoring either unconditional cue fails at the intended runtime assertion.

The runner retains Sources, logs, per-control results and hashes and replaces the
old destructive temporary-directory trap with canonical completed-run cleanup.
Worker frame-order11 and partial-order51/318 checks pass. A mistaken initial
runner group selection was refused before executing fixtures; that preflight
failure remains retained. No source repair or assertion weakening was needed.

Root reviews source and the compiled delta, matches archived fixture/source
hashes, runs strict Debug/Release and integrated source checks, then pushes dev.
Final hosted status and cleanup/delivery receipts remain in ignored evidence to
avoid a second source-validation cycle for a status-only tracked note. The prior
680 complete hosted checks, bounded681 synthetic-source proof, and unchanged
native3496/projection471/remote836 and300,518 wire golden assertions are inherited.
