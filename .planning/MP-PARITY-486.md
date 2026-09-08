# Multiplayer 1:1 review — ModBuild 486

## Requirement and review scope

The user requested intermediate native animation parity, followed by a systematic review and
immediate repair of further divergences. Only explicitly approved exceptions permit a different
picture. Performance deferrals and comments describing an approximation do not grant approval.
The rule is recorded in `AGENTS.md` for future work.

The review compared owner and observer implementations for original use bars and their pickers,
decisions, initiative, objectives, elements, board furniture, active/held/round/pile cards,
hand/item/browser fans, burns, semantic flights, particle effects, and shared story/map windows.
Workers used separate worktrees; the master reviewed and integrated their changes into `dev`.

## Closed source-proven gaps

- Original active-bonus movement, scaling and other native animated values now reach the original
  remote prefab targets throughout the animation. Auxiliary ability, augmentation and item bars
  also retain their original previews, pickers, child widgets and actual animation output.
- Element creation, availability, ordering, native layout and graphical effects follow the owner.
  Initiative depth uses the owner's exact value and original authored depth, including when the
  viewer has flattened their own initiative row.
- Original widget fitting, native hover/press behavior, sprite transitions, font materials and
  color fades are preserved. Source stages update before every mirror sync; received values are
  applied after fitting/painting. Rejected procedural substitutes cannot reappear on recovery.
- Remote content updates follow received and native content changes immediately. The former
  viewer-controlled content delay remains as an INERT configuration key in both languages.
- Fan titles, character exchanges, resident membership reflow, active-card identity and held-card
  returns now match their owner paths. Semantic and burn flights preserve the owner's captured
  endpoints and orientation.
- Card smoke uses original native emitters, authored modules, actual activation/seed/clock, emission
  state and owner-relative transforms. Item foreground shader effects are retained as well.
- Shared windows retain ownership from the first movement, honor held stationary grips and
  interpolate received movement and size without inventing motion across idle gaps.

Detailed source evidence, implementation contracts and exceptions are recorded in the
[card ledger](MP-PARITY-486-CARDS.md), [native-widget ledger](MP-PARITY-486-NATIVE.md),
[board ledger](MP-PARITY-486-BOARD.md), and [shared-window ledger](MP-PARITY-486-SHARED.md).
The original animation request is described in [MP-ANIMATION-486.md](MP-ANIMATION-486.md).

## Transport and compatibility

GVR1/version 3, the rig packet and existing presence record meanings stay unchanged. New
presentation frames use records 49–52 with independent atomic reassembly. Auxiliary records
are per slot. Small pages share a message-9 batch within the existing 864-byte/50-ms cosmetic
event limit; slow frames never trigger a catch-up burst. The element frame uses bounded lossless
compression, preserving actual float values. Clears and identity boundaries survive coalescing
on both sender and receiver. Presentation-only reassembly has a bounded 32-second budget,
validated under saturated 90/18-Hz rendering; the game connection timeout is unchanged. Final-state repetition recovers lost state without replaying a tween.

Public effect/action selectors resolve already replicated native content. Private card names,
art identifiers and arbitrary native object paths are not added to the wire. Original game
controllers and callbacks remain stripped from remote clones. Configuration keys and shipped
assets are unchanged. Installation is DLL-only after the full ModBuild-483 asset installation.

## Validation and remaining evidence

The integrated suite passes **238,556 assertions**; all 17 static/surface checkers pass. The strict
Release build has zero warnings/errors. Patch inventory remains 107 classes/165 methods, with
625 configuration keys. Five diagnostic tokens were added; none was removed (4,709 total).
The compiled-form guard reports intentional feature changes against `66c3bfbf`; its nonzero
diff exit is expected and was inspected. Documentation parity and all 16 metadata-only reference
assemblies also pass their checks. The tests include exact packet bytes,
complete-frame rejection, bounded reassembly, clear/reopen boundaries, fan membership/reflow,
shared-window motion and native pointer/depth behavior. Negative controls demonstrate that the
new behavior checks reject their corresponding regressions.

Both supplied multiplayer logs are ModBuild 482. They establish the original failures and a UDP
receive timeout, but cannot validate this build's headset rendering. No automated test proves
pixel-level equality, actual Unity particle output or two-client animation timing. Those outcomes
still require the next headset test. The underlying reason transport receipt stopped in the
reported disconnect remains unresolved; see [the original investigation](MP-ROUND-484.md).
