# Refactor execution log

> **Last verified 2026-09-08 against `49ceab21` (ModBuild 483).** This file is the record of the
> **2026-07 programme** and is read as history, not as status. Two later programmes exist
> (`LOG-2026-08.md`, and `.planning/refactor-2026-09/`); see `CHARTER.md` section 6.
>
> The audit re-ran every checker and re-checked every open item. **Two statements below are now
> false and both are marked in place**: the checker-status block (its numbers are the 2026-07
> totals) and the "compiler-checked doc comments" entry under *Claims that did NOT reproduce*
> (the property was later made to work and is on today). The open-items list has a verified
> status column appended at the end of this file.

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

Checker status at the end of E — **a 2026-07 snapshot; see the current numbers below the block**:

```
patch surface: 34 classes, 56 methods, all registered exactly once
frame order:   7 locked orderings verified against source and lock
mirrors:       3 mirrored-constant groups agree
wire tests:    188 assertions passed
```

> **[verified 2026-09-08]** The same four checkers at `49ceab21`, run for this audit. Quote these,
> not the block above — the block is what those numbers were when batch E closed. Every one of them
> has grown since, and by wildly different factors (patch surface 3x, frame order 1.6x, mirrors 13x,
> wire assertions 1100x), so none of the old numbers can be scaled into a current one:
>
> ```
> patch surface: 107 classes, 165 methods, all registered exactly once
> frame order:   11 locked orderings verified against source and lock
> mirrors:       41 mirrored-constant groups agree; 7 shared-expression groups have one
>                implementation each; 2 subset-guard groups keep the game's terms together
> wire tests:    210 164 assertions passed
> ```
>
> There are now **seventeen** checkers, not four. The other thirteen, all green at `49ceab21`:
> `check-partial-order` (23 multi-part types, 179 parts, 0 cross-part dependencies),
> `check-instrument-writes` (549 diagnostic-written fields, 61 load-bearing, baseline 61),
> `check-enum-arrays` (15 literal-sized arrays, 15 agree),
> `check-mirror-dials` (7 live reads under `Net/Remote`, 1 still OPEN),
> `check-hw-verify` (615 marked lines across 207 files, all above the default level),
> `check-options-coverage`, `check-tune-fields` (145 ids, 131 sampled in ascending order),
> `check-remote-defaults` (95 frozen constants), `check-wire-coverage` (194 board dials on
> record 28, 80 exempt, 0 PENDING), `check-desync-surface` (17 patch classes on 37 receiver
> types, all classified), `check-card-identity-mask`, `check-surface`, `check-bundle-format`.
> The authoritative list is the call sequence in `scripts/refactor-guard.sh`.

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
  > **[verified 2026-09-08] SUPERSEDED — and by exactly the proof this entry demanded.** The
  > property was made to take effect on 2026-08-27 (`LOG-2026-08.md` section 3.2, commit
  > `4d87cf76`) and `Directory.Build.props:82` carries `<GenerateDocumentationFile>true</GenerateDocumentationFile>`
  > today. The finding above is still correct about what was measured at the time and is kept for
  > it: **"zero CS1574" from a check that never ran looks identical to "zero CS1574" from a clean
  > tree**, and the only thing that separates them is a deliberately broken cref. That is the
  > lesson, and it survived the reversal. Its residue is `STALE-DOC-REFS.md`.
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


---

## Status of "Open items routed to the user" — **[verified 2026-09-08 against `49ceab21`]**

Re-checked against source, because a list of open items nobody re-reads becomes a list of
problems everybody believes are still open. Four of six are closed.

| Item | Status at `49ceab21` | Evidence |
|---|---|---|
| `PalmGate.UseDevicePalmNormal` half-removal | **CLOSED** | `grep -rn "UseDevicePalmNormal" src/` returns nothing. Both halves are gone. |
| `ComfortSettings` tells the user to press the grip | **CLOSED** | `ComfortSettings.cs:304` now reads *"THE BUTTON IS THE STICK CLICK, not the grip: … which moved it off the grip so a grip could stay reserved for grabbing things."*, and `:155` carries the same correction in the summary. |
| `docs/TESTING-P4.md` section 5 asks for a comfort vignette that does not exist | **CLOSED** | `grep -i vignette docs/TESTING-P4.md` returns nothing. |
| `BoardScale` fresh-install vs migrated: bind default `0.5`, migration writes `0.4`, marker key says `04` | **SUPERSEDED, half open** | The default is neither 0.5 nor 0.4 any more: `Defaults.Cards.cs:297-299`, `BoardScale_{Oak,Steel,Bronze} = 0.54265f`, hardware-tuned. The marker key **still** says `04` — `BoardScaleDefault04Applied` (`:347`, pinned, *"a fresh install must start false"*). That is now deliberate rather than a discrepancy: renaming a one-shot migration marker re-runs the migration on every existing install. **Do not rename it.** |
| Item-fan vs browse-fan scale divergence (rig scale vs board scale on a board-anchored fan) | **NOT RE-CHECKED in this audit** | Documentation audit only; this needs a hardware round and is Tier 3, exactly as recorded above. |
| The remote board shows a pick field the local board never showed | **NOT RE-CHECKED in this audit** | Same. Note that `check-mirror-dials.py` now exists and reports **1 OPEN** verdict among 7 live reads under `Net/Remote` — that gate is the place to start, not this line. |

The two "NOT RE-CHECKED" rows are marked so rather than left silent: this pass verified
documentation against source and did not run a hardware round. **An unverified row is not a
closed row.**