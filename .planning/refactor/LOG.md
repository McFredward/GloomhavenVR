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

## Batch F — motion (complete)

| Subsystem | Result | Guard |
|---|---|---|
| Net | `RemoteBoardContent.cs` 1 254 → 108 + 6 files | **nothing** |
| Rig | `VRRigDriver.cs` 1 277 → 720 + 3 partials | `MOVED` — see below |
| Cards | `CardsDriver.cs` 5 491 → 6 parts; `PlayTray.cs` → 7; 3 stowaway types out | **nothing** |
| WorldUI | `FlatScreen` → 6, `SettingsPanel` → 6, `CanvasConversion` → 4 + 4 types out, `ModalFallback` → 9, `FlatScreenStereo` → map/compositor | **nothing** |

Largest file in the mod: **2 254 lines** (was 5 470). 197 → 247 files, same total line count.

### The pass condition was too lax, and it nearly cost a real regression

My brief said "`MOVED` is expected, `0 changed` is the pass condition." The Cards worker's splits
came back **empty** instead, and pointed out why that distinction matters:

**Field initialisers run in declaration order, and across partials that order follows compile
order — so a reordered initialiser also shows up as `MOVED`.** My pass condition accepted exactly
the output a broken split produces. Only an **empty** diff proves the compile order survived.

I tightened it mid-flight and messaged the WorldUI worker, which had already committed three
splits as `MOVED`. It reset and redid them — and **one was genuinely wrong**: `CanvasConversion`'s
decompiled field table had `LastMaskExclusions` and `DiagSb` swapped. A reordered static
initialiser, classified `MOVED`, passing under the old rule. That file is the worst case for it,
because its seven shared scratch buffers are declared beside the sweeps that use them and are
therefore spread across all four parts.

Two measured facts that make this concrete, both from running it rather than reasoning about it:

- **MSBuild sorts glob results OrdinalIgnoreCase.** `FlatScreen.Pointer.cs` sorts *before*
  `FlatScreen.cs` — so the obvious naming scheme silently reorders initialisers. Digit-prefixed
  parts (`.1.`, `.2.`, …) are what make the glob reproduce the original order.
- **ilspycmd emits all fields before all methods** (separate metadata tables), which is why a
  field reorder is visible in the snapshot at all.

### Why `VRRigDriver` still reads `MOVED`

It was split before the tightening, with all instance fields kept in the primary file. Re-verified
under the stricter rule: **0 field-shaped lines** in its moved diff — every repositioned line is a
whole method body. It is safe; it reads `MOVED` only because its parts are named by topic rather
than by digit, so the glob concatenates them in a different order than the original. Left as is:
re-splitting a verified-safe 1 277-line file to make one guard line prettier is the kind of churn
the charter exists to prevent.

### Deviations, all in the direction of moving less

- Part counts rose above the reviews' proposals (5→6, 5→6, 3→4, 8→9). Every review group that
  spanned two non-contiguous source runs had to become two parts — **no member was reassigned to
  make a grouping look tidier.**
- `FlatScreenStereo` stopped at the one map/compositor cut, as planned. Its field region was
  deliberately **not** split: map probe consts, a nested type and compositor render textures
  interleave there, and reordering that region is precisely the hazard above.
- The reviews' suggestion to pin compile order with explicit `<Compile>` items was **rejected**:
  the SDK glob would double-include unless default items are disabled, a far more invasive build
  change than a naming rule that is proven to work.

### Follow-ups left open (guard-empty, not done because Batch F was scoped to pure motion)

- Restate the `WantedQuadWidth` "one function, both callers" invariant at the function's new home.
- `SettingsPanel` part 1 needs a pointer that `Build()` must clear `_refreshers` / `_debugRows` /
  `_debugRowVisible` first — an NRE-flood fix whose `Build()` now lives two files away from the
  lists it guards.
