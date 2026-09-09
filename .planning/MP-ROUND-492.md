# ModBuild 492 — rest appearance and build-491 hardware evidence

## Scope and evidence

The user reports their best multiplayer run so far, with spent rest cards turning blue,
a single whole-board disappearance during a teammate's long-rest burn, and possible lower
performance while streaming. Both current `LogOutput.log` banners identify build 491 at
line 17. Evidence is in the ignored main-checkout `.planning/debug/` and `remote/` folders.
No new screenshot captures the disappearing board; the existing regression JPGs belong to
the preceding build-490 report and cannot establish this incident's cause.

## Rest cards

The short-rest offer is an adopted original `ShortRestedCardWidget`, but does not
carry a `VRCard.PileOrigin`. The game obtains it from the same hand's card widgets. The owner
appearance sampler previously dropped it when hand/active/round addressing
failed. A receiver awaiting owner output cleared its inherited decoration, producing a blue
card instead of the owner's spent look.

The appearance-only fallback now resolves the exact card model in its own actor's canonical
discard/lost arc. It uses the same ordering and count as the receiver. It does not change
held-card identity, privacy, gameplay ownership or pile membership. Duplicate/absent models,
unknown-seat sentinels and count overflow cannot publish guessed addresses. Tests also cover
a separate dialog widget for the same model and a discard-to-lost transition; model identity
remains authoritative even if a future dialog uses a different widget instance.

The original game's burn start calls `RestoreCard`, resetting spent material channels before
the burn timeline ramps up. The burn lane preserves the actual pre-reset spent base through
that transition, locally and on the remote mirror. Native completion and cancellation must
continue to use native progress, independently of the cosmetic spent floor.

Local controlled cards and the entire map remain public. Remote scenario selection and
explicit short-rest burn flights retain the user's concealment rule. A spent appearance
does not reveal a private card's identity. Flight routing and completion timing are unchanged.

Implementation and regression coverage: [MP-492-BURN.md](MP-492-BURN.md).

## Brief whole-board disappearance

Both long rests were matched between owner and observer logs using the actual prompt and
burn-flight events. The relevant teammate burn is GrabandGo: remote owner 32788–33623,
main observer 58856–60318. No corresponding style/tuning rebuild, scenario-gate close,
peer fade edge, reconnect, or Unity/device-loss error was found. A 90.37 ms frame at main
60135 is a measured stall, not proof of a missing board or its cause.

Build 491 did not record every root activation edge. Build 492 adds `REMOTE BOARD VISIBILITY`
only on creation, actual activation changes and destruction requests, with reasons and root
state. It does not change visibility behavior. This incident remains unresolved pending a
reproduction; no speculative graphics fix is claimed.

Full event correlation and limitations: [MP-492-BOARD.md](MP-492-BOARD.md).

## Performance

Across completed reporting windows, mean frame time is 14.26 ms main / 21.05 ms remote.
Named mod work accounts for 5.11 / 5.83 ms respectively. The remote runtime repeatedly
reports lower presentation rates, often 36 instead of 72 Hz; comparing over-budget counts
without accounting for this changing budget would misrepresent the run.

There are measurable card, board and environment stalls, plus heavy diagnostic output.
Neither a matching older-build baseline nor encoder/compositor timings exist. Streaming
causality and the size of a build-to-build regression cannot be established. The sampler
now has its own `Net.CardAppearance.Sample` timing scope, alongside clone `.Build` and
playback `.Apply` scopes, to separate native appearance work from broader frame cost in
the next run. Sampling cadence and parity are retained.

Weighted statistics, examples and allocation/logging candidates: [MP-492-PERF.md](MP-492-PERF.md).

## Other log findings

- Both `LogOutput.log` files contain no `[Error` entries. The native card clone/group failures
  from build 490 are absent. No actual `PACKET REJECTED`, burn-completion hang, or lost-card-FX
  event was found; explanatory text mentioning these tokens is not an occurrence.
- Main has 32 `DESYNC STALL` warnings belonging to 11 waits, all for `StatesSynchronized`.
  All 11 have a matching `DESYNC STALL CLEARED`, after 2.6–153.3 seconds; remote has none.
  Phase changes continue during the longer waits. The old prose incorrectly declared every
  halted processor a dead session. It now describes the current pause, normal recovery and
  the separate evidence required for a failed burn coroutine. Watch thresholds are unchanged.
- `ActionSelection: NO action card docked` appears 87 / 61 times. Inspected contexts include
  cards leaving the round for active/pile state and phase transitions; the warning alone does
  not prove a blocked turn. This review does not label these as 148 gameplay failures.
- Recurring wall-fade diagnostics dominate warning volume, including released foliage,
  leftover renderers and latch warnings. They identify candidates for a visual reproduction,
  not a proven cause of the board incident. The previously hardware-approved wall behavior
  is unchanged. Wall logs alone account for approximately 44.8 MB on each side.
- Each client reports one guarded `ActorBar OnUpdatedZoom deferred` during initial adoption.
  The main also records guarded missing display-quad diagnostics during startup. These are
  distinct from an uncaught render failure during long rest.
- Remote 17358 reports unavailable native element output during gameplay; 17359 records three
  blank elements, and 17366 shows recovery. The material binder used a potentially stale clone
  material after an inactive source branch had been skipped. It now validates and clones the
  exact original graphic's material and refreshes owned copies when that source changes.
  This closes the source-proven dependency; the old warning cannot name the exact failed node.
  New diagnostics identify element, graphic and shader if the original itself is unavailable.
- Native `Player.log` contains repeated Hydra environment-service DNS failures in the rest
  windows. They are not evidence of a multiplayer transport disconnect or a board hide.

## Verification

- All 17 `refactor-guard.sh check --summary` checkers passed. Its expected nonzero final exit
  reflects intentional compiled differences against `050e8801`: 16 changed types, four new
  helpers, none removed. Five changed types contain only the inlined 491→492 build constant.
- Complete wire suite: **251,797 assertions** (45 additional checks: 11 appearance addresses,
  eight element source/material seams, 26 spent-history/native-enumerator cases).
- Production native-card hierarchy harness: **883 assertions**; reinstating the old eight-group
  limit fails its runtime negative control. Removing the spent floor also fails the worker's
  private runtime negative control.
- Final integrated strict Release: **zero errors, zero warnings**.
- Documentation i18n passes; all 16 game reference assemblies remain metadata-only.
- Patch registration: **109 classes / 167 methods**; patch surface **152**. The two additions
  are the native card-effect reset/iterator hooks, both registered exactly once by CardsModule.
- Config keys **625**, log tokens **4,716**, load-bearing instrument baseline **61**. No old
  config key, patch or grep token was removed. Bundle remains **74,943,763 bytes**.

Automated tests establish source contracts and regression cases; the spent-burn picture,
element rendering and the disappearing-board incident still require hardware observation.
