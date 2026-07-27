# Refactor execution log

Charter §7 requires the guard output per commit. It lives in the **commit messages** — the four
parallel workers correctly refused to write into one shared file, which would have collided.
This file records the per-batch totals and the decisions taken during execution.

## Batches A–E (complete)

| Batch | Scope | Guard result |
|---|---|---|
| A | checkers: patch inventory, unregistered-patch parse, frame-order lock, wire vectors, mirror lint | **nothing changed** |
| B | wire-enum defences, reserved bit, classification tags | 3 changed — **three const declarations, no method body** |
| C | ~40 stale comment clusters + 2 stale `docs/INTERFACES-*.md` | **nothing changed** |
| D | dead code | only the intended types, **+0 lines in every deletion** |
| E | 49 legacy config descriptions re-labelled, **none unbound** | one line per description |

Totals: **~1 400 lines removed**, build warnings **4 → 0**, 11 changed types across the whole of
C/D/E — every one predicted in advance.

Checker status at the end of E:

```
patch surface: 34 classes, 56 methods, all registered exactly once
frame order:   7 locked orderings verified against source and lock
mirrors:       3 mirrored-constant groups agree
wire tests:    188 assertions passed
```

## What the execution corrected in the plan

Four workers ran the plan rather than reading it, and each found something the plan had wrong.

1. **`worktree-setup.sh` reported success and did nothing** for `libs/RuntimeDeps` and
   `libs/Natives`: both are *tracked* (a `.gitkeep` and a README) while their payload is ignored,
   so the existence test skipped them. Three separate workers hit it. Fixed to link contents.
2. **The patch-inventory checker failed on shifted line numbers.** The first unrelated commit
   after it landed tripped it — two lines moved in `CardsModule`, no patch added, removed or
   unregistered. It now warns for line-only drift and still hard-fails on a real one; both paths
   verified. Same failure mode as the guard's `-dirty` false positive, one day apart: **a checker
   that fires on the harmless teaches people to bypass it.**
3. **Batch E's scope was much larger than the plan said** — the plan named Cards and
   `FlatScreenStereo`; there were 22 more in `Plugin [Hands]` and `[FigureGrab]`. Two traps
   inside them: `[Hands] {Style}Scale` and `[FigureGrab] HeldUpright` sit *among* the legacy
   entries and are **live**. A `.Value` grep does not find their readers, because the read is
   inside the entry's own config file. Both survived.
4. **Batch D for Net/Rig was essentially empty** — the plan had no Net/Rig row and the brief
   asked for one. Independent scans found exactly one dead member (`RemoteHandFan.PalmStandoff`),
   named in neither the review nor the registry. It lost its caller *because using the bare
   offset was a hardware bug*.

## Claims that did NOT reproduce — deliberately not adopted

- **Compiler-checked doc comments.** The Net/Rig review proposed building with
  `GenerateDocumentationFile=true` as a fifth checker, reporting "zero CS1574 across the mod".
  Tested: with a **deliberately dangling cref**, a full rebuild emits no `CS1574`, no `CS1570`,
  and **no XML file at all** — with the property set on the command line *and* with
  `DocumentationFile` pointed at an explicit path. The reported "zero" is what a check that never
  ran looks like. Not added to the guard. If someone wants this, the property has to be made to
  take effect first, and the proof is a deliberately broken cref producing a warning — not a
  clean run.
- **`DisableAllMouses` is not dead code.** It is `InputManager.DisableAllMouses`, a *game* API. A
  `src/`-only sweep reports every game symbol as dangling — the sweep was the defect.
- **Batch A's "five-way" mirror group is four.** The lint's table always listed four; the prose
  was wrong. Verified by sweeping every `const float … = 0.008f`.

## Open items routed to the user, not decided here

- **`PalmGate.UseDevicePalmNormal` retirement is incomplete.** The dead write lives in `Cards/`
  and the field in `Hands/`; the two halves were owned by different workers and a half-removal
  does not compile. The instruction and the completed sweep are recorded on the field.
- **The item-fan vs browse-fan scale divergence** (rig scale vs board scale on a board-anchored
  fan) is probably a bug, not a style split, and needs a hardware round. Documented on both
  methods, deliberately not fixed — Tier 3.
- **The remote board shows a pick field the local board never showed** — a real multiplayer
  inconsistency that predates this work. Noted at the removal site, not touched.
- **`ComfortSettings` still tells the user to press the grip** for what has been the thumbstick
  click since `8d28b55` (the grip is now figure-grab, so following the text picks up a mini).
  User-visible, non-empty guard diff, so it does not belong in a batch whose expectation is
  "nothing changed".
- **`BoardScale` fresh-install vs migrated discrepancy**: bind default `0.5`, migration writes
  `0.4`, marker key still says `04`. Documented, not harmonised.
- **`docs/TESTING-P4.md` §5** still asks the user to test a comfort vignette that no longer
  exists.
