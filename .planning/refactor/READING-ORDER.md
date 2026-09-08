# Reading order — `.planning/refactor/` and `.planning/refactor-2026-09/`

> **Written 2026-09-08, verified against `49ceab21` (ModBuild 483).** This directory holds 25
> files, and `.planning/refactor-2026-09/` holds the newest round’s. Between them they span three
> refactor programmes, and there is no way to tell from the filenames which are
> **law**, which are **history** and which are **spent**. That is what this page is for. It is an
> index, not a summary: it says what to open and in what order, and every file it names carries
> its own dated verification header.

## Which programme is which

| | 2026-07 | 2026-08 | 2026-09 |
|---|---|---|---|
| what it was | first refactor; charter, guard, registries | splitting the files the first round never reached | review-then-fix, five parallel lanes, **defects in scope** |
| rules | `CHARTER.md` | `CHARTER.md` | `CHARTER.md` **as amended by** `.planning/refactor-2026-09/BRIEF.md` §1 |
| plan / log | `PLAN.md` / `LOG.md` | `PLAN-2026-08.md` / `LOG-2026-08.md` | `BRIEF.md`, then `REVIEW-<lane>.md` |
| hardware pass | `HARDWARE-REGRESSION.md` *(spent)* | `HARDWARE-REGRESSION-2026-08.md` *(spent)* | `HARDWARE-REGRESSION-2026-09.md` |

## Read in this order

1. **`CHARTER.md`** — the rules everything still runs under. Its §2 (why the default answer to
   "should this be restructured?" is **no**), §4 (the tiers) and §3b (where the guard is blind)
   are the parts that govern. §1's line counts are the 2026-07 starting state and say so.
2. **`.planning/refactor-2026-09/BRIEF.md`** — what the most recent round changed about those
   rules. Three things matter: source-demonstrable **defects are in scope**, **parallel
   construction** is in scope, and **comments are load-bearing** — the tree is ~45 % comment by
   design and a comment is only ever changed when it is factually wrong.
3. **The registry for the code you are about to touch.** These are vetoes, not notes:
   - `INVARIANTS-Cards.md` · `INVARIANTS-WorldUI.md` · `INVARIANTS-Net-Rig.md` ·
     `INVARIANTS-Hands-Board-Core.md` · `INVARIANTS-2026-08-SplitTargets.md`
   - **Cards and WorldUI open with a STALE SYMBOL INDEX. Read it first.** Both were written
     against a much smaller tree, and 17 (WorldUI) and 21 (Cards) of the symbols they name no
     longer exist. The index says what took each job over.
   - `CardsGameApi` and `CardFan` have no entry in these files. Theirs are in
     `.planning/refactor-2026-09/REVIEW-cards.md` §3.
4. **The gate**, before you believe anything is fine:
   ```
   bash scripts/refactor-guard.sh check --summary     # 17 checkers, then the compiled-form diff
   EXPECT_WARNINGS=0 bash scripts/ci-build.sh
   python3 scripts/check-docs-i18n.py
   ```
   A first run in a fresh worktree needs `bash scripts/worktree-setup.sh`.

## Two habits this directory exists to enforce

- **Take every number from the tool, never from a document.** Counts here are stamped, not
  maintained, and at least one comparison in this directory was apples-to-oranges because the
  *instrument* changed rather than the tree (`LOG-2026-08.md`, the config-key row).
- **A gate's green reading is a hypothesis until it has been seen going red.** Two shipped guards
  were found green-on-red in 2026-09; `CHARTER.md` §3 records both, with the falsifying
  experiment.

## Reference, not reading

`FRAME-ORDER.lock`, `INSTRUMENT-WRITES.baseline`, `ENUM-ARRAYS.allow`, `PARTIAL-ORDER.allow`,
`MIRROR-DIALS.allow` are **gate inputs**. They are machine-checked, so their entries cannot go
stale silently — only their prose can. Do not hand-edit one without running its checker.

`STALE-DOC-REFS.md` is the residue of turning on `GenerateDocumentationFile`: two open dangling
crefs, each now naming its replacement.

## Spent — do not act on these

`PLAN.md` (executed; read §0's conclusions, not its work list) · `CENSUS.md` (2026-07 candidate
lists; read the method note, which is a live trap) · `HARDWARE-REGRESSION.md` and
`-2026-08.md` · `MENU-STRUCTURE.md` (**a proposal against `SettingsPanel`, which is deleted**;
the live menu is `WorldUI/Options/VROptionsTab.*` and its authority is
`scripts/check-options-coverage.py`) · the four 2026-07 `REVIEW-*.md` (they say "nothing here has
been applied"; `LOG.md` records what was).

`.planning/refactor-2026-09/NEEDED-OUTSIDE-*.md` are **mixed**: some entries have been applied and
the files did not say so. Each now carries a spot-checked status header. **One is a live defect —
`NEEDED-OUTSIDE-cellar-one-sky.md` §1.**
