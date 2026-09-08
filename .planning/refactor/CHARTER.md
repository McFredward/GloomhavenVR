# Refactoring Charter

> **Last verified 2026-09-08 against `49ceab21` (ModBuild 483).** An audit is a snapshot;
> what follows was true at that commit. Corrections made in this pass are marked
> **[verified 2026-09-08]** where the old sentence had gone wrong; everything unmarked was
> re-read and left alone because it was still true.
>
> **§1's numbers are the 2026-07 starting state, not the current one.** At `49ceab21` the mod is
> **621 `.cs` files / 551 166 lines** under `src/GloomhavenVR` (~255 600 of them non-comment).
> The growth is features and load-bearing comment added by the hardware rounds since, not
> refactoring debt — but do not quote §1 as a description of today's tree.
>
> The rules this refactor operates under. Written before any code was touched.
> Companion documents: the five registries — `INVARIANTS-Cards.md`,
> `INVARIANTS-Net-Rig.md`, `INVARIANTS-WorldUI.md`, `INVARIANTS-Hands-Board-Core.md` and
> `INVARIANTS-2026-08-SplitTargets.md` — (what must not change), `REVIEW-*.md`
> (per-subsystem findings), `PLAN.md` (the ordered work list), `LOG.md` (what was
> actually done, commit by commit). *There has never been a single `INVARIANTS.md`; the
> original sentence named one and a successor would look for a file that does not exist.*
> The 2026-09 round's own artefacts live one directory over, in `.planning/refactor-2026-09/`.

## 1. Goal

Make 85 600 lines across 197 files readable and maintainable — without changing
what the mod does, by a single line of behaviour.

**In scope:** dead code removal, file/type decomposition, duplication removal,
naming, layering, cohesion, and documenting the load-bearing logic.

**Out of scope:** features, tuning values, frame ordering, timing, wire format,
Harmony patch targets, anything the user would have to re-test on hardware.

## 2. The core risk, stated plainly

This codebase has **no automated tests** and cannot have meaningful ones: nearly
every behaviour is a negotiation with a decompiled Unity game, a headset, and a
human's perception of it. Twenty-six hardware test rounds produced the current
code. Large parts of it look arbitrary and *are not* — they are the residue of a
bug that took several rounds to corner.

Examples of logic that reads like an accident and is in fact the fix:

- The **resting** card rect is used for laser-driven pops and the **live** rect
  for hand-driven pops. Using one for both re-opens a feedback loop.
- `SuppressFarClick()` exists to separate "claim the trigger" from "move the
  beam" — collapsing the two brings back the phantom wall through the card.
- The extras packet's additive blocks are written *and* read in flag-bit order.
  Any reordering silently corrupts every peer's state.
- Hover dispatch walks leaf → common root. Dispatching to the hit object alone
  is what broke the game's hover animation for months.

**Therefore the default answer to "should this be restructured?" is no.** The
burden of proof is on the change, not on the status quo. A file being long is
not, by itself, a reason to touch it.

## 3. The safety net: compiled-form diffing

`scripts/refactor-guard.sh` builds Release, decompiles the DLL back to C# with
`ilspycmd -p`, masks the build stamp, and diffs against a stored baseline.

**[verified 2026-09-08] `check` is no longer only the diff.** Before it builds, the script runs
**seventeen checkers**, each of which hard-exits on failure — `patch-inventory.sh`,
`check-frame-order.sh`, `check-mirrors.sh`, `check-partial-order.py`,
`check-instrument-writes.py`, `check-remote-defaults.py`, `check-wire-coverage.py`,
`check-tune-fields.py`, `check-desync-surface.py`, `check-hw-verify.py`,
`check-options-coverage.py`, `check-card-identity-mask.py`, `check-mirror-dials.py`,
`check-enum-arrays.py`, `wire-tests.sh`, `check-bundle-format.sh` and `check-surface.py`
(snapshot + diff). Each exists because of a named defect that shipped; the reason is written at
its call site in the script, and that call site is the authoritative list — this paragraph will
go stale before the script does. The full gate at `49ceab21` is
`bash scripts/refactor-guard.sh check --summary`, plus `EXPECT_WARNINGS=0 bash scripts/ci-build.sh`
and `python3 scripts/check-docs-i18n.py`.

Because `-p` groups output **by namespace and type**, the snapshot is completely
independent of our source file layout. That yields the property this refactor
is built on:

| Refactor kind | Expected guard output |
|---|---|
| Move a whole type to another file | **nothing at all** — the snapshot files types separately |
| Split a class into partials, reorder members | **`MOVED`** — same lines, different order |
| Rename a private member | `CHANGED`, confined to that one type, names only |
| Extract a method | `CHANGED`, confined to that one type |
| Anything appearing in a type I did not intend to touch | **collateral damage — revert** |

**Corrected after measuring, not assuming** (the first version of this table was wrong twice):

- `ilspycmd -p` emits members in **source order**, so splitting a class into partials or
  reordering members reshuffles the snapshot even though nothing compiled has changed. Without
  a distinction, every Tier 1 motion would look like a failure. The guard now classifies each
  changed file: `MOVED` when the two versions are **permutations of the same lines**, `CHANGED`
  otherwise. Verified by swapping two methods in `VRLayers` — reported as `MOVED`, 0 changed.

  **The safety rule for a partial split is narrower than "keep each run contiguous."** I first
  wrote that rule together with "name the files so alphabetical order reproduces member order",
  which is awkward and mostly unnecessary. The Net/Rig worker replaced it with the condition
  that actually bites: **member order in a partial class is semantically inert *except* for
  field initialisers, which run in declaration order** — and across partials that order follows
  compilation order. So the rule is: **every instance field stays in the primary file, in its
  original order.** Methods may then be distributed in any grouping, contiguous or not.
  Verified on `VRRigDriver`: 422 lines repositioned, **zero of them field declarations**, every
  moved line a whole method body.
  *This is not a safety proof:* swapping two statements that DO depend on each other is also a
  permutation. It narrows "what changed" to "only the order changed" — which is the question a
  human then has to answer, and is exactly where frame-ordering constraints live (§3b.2 —
  *the original text said "§8", and there is no §8; the frame-ordering blind spot is §3b.2 and
  the lock file is `FRAME-ORDER.lock`*).
- Config `Bind` descriptions are **string literal arguments**, so they DO appear in the
  snapshot. XML doc comments and `//` comments do not (measured: 0 `<summary>` tags in the whole
  snapshot). Implicit sequential enum values are not rendered, so making them explicit is
  provably free — which is what makes the `PileKind` wire fix free.

Verified: two builds of identical source differ only in the build timestamp and the baked commit
hash, both of which the script masks. A no-op check reports "no compiled behaviour differs".

This does not prove a refactor is *correct*. It proves the exact blast radius,
which is the thing that is otherwise invisible and is how regressions get in.

**[verified 2026-09-08] …and for a time it did not prove even that.** The 2026-09 tooling review
found the guard capable of printing a GREEN verdict on a RED build, twice over, and both are
fixed. Keep both, because both are the same mistake and it is easy to make again:

1. **A `grep` standing in for an exit status.** The build ran as
   `dotnet build … | grep -E "error|Build FAILED" && { echo "error: build failed"; exit 1; }`.
   Under `set -euo pipefail` that can NEVER fire on a failed build: `pipefail` makes the
   pipeline's status `dotnet`'s non-zero exit, so `&&` skips the block, and `set -e` does not act
   on the left-hand side of `&&`. The block fired only when `dotnet` **succeeded** and its output
   happened to contain the substring "error" — so a build that failed sailed on to diff a stale
   DLL and reported "no compiled behaviour differs from the baseline". Falsified with three
   stubbed `dotnet`s: `{echo "error CS1"; exit 1}` continued, `{exit 1}` continued, and
   `{echo "error-prone"; exit 0}` aborted — exactly inverted. It now branches on the exit status
   and uses the grep only to *print* the reason.
2. **`diff … || true` swallowing the verdict.** Plain `refactor-guard.sh check` exited 0 on a
   differing snapshot, so `refactor-guard.sh check && commit` passed on anything; only
   `--summary` carried a real status. Plain `check` now exits 1 when the snapshot differs.

The lesson the charter should have carried from the start: **a gate's own green reading is a
hypothesis until the gate has been observed going red.** Every checker added since has been run
against a deliberately broken input before being trusted.

**Rule: every refactor commit records its guard output.** In practice that is the **commit
message**, not this file — the four parallel workers refused to share one file and were right to
(see `LOG.md`'s opening). `LOG.md` carries per-batch totals and decisions.

## 3b. Where the guard is blind

Stated plainly, because a safety net you trust too far is worse than none:

1. **It proves blast radius, not correctness.** Inside a type I meant to touch, a wrong change
   looks exactly like a right one.
2. **Frame ordering.** The order in which subsystems run per frame is a documented constraint in
   several places (`VRHand`'s six interactors, `BoardDriver`'s five steps, `FigureGrab` after the
   Animator, MixedReality last in `_tailSteps`). A reordering shows up as `MOVED` or as an
   ordinary `CHANGED` inside an expected type. **This is the refactor's blind spot**, and it is
   why ordering constraints get their own checker rather than relying on the guard.
3. **Anything not in the assembly**: the Harmony registration wiring (a patch class nobody
   references compiles and ships inert — that has happened twice), config keys disappearing from
   a user's `.cfg`, log grep tokens the debug workflow depends on.
   **[verified 2026-09-08] These three now have checkers and are no longer blind** — that is the
   whole reason `patch-inventory.sh` and `check-surface.py` exist, and `check-hw-verify.py`
   additionally catches the case the original sentence could not imagine: a grep token that still
   exists but sits below the DEFAULT log level, so the hardware round it was written for comes
   back silent (ModBuild 331/334). The item stays because the *class* of failure is still real:
   anything user-facing that the assembly does not carry is invisible to the diff, and the
   remedy is always a text checker, never the guard.
4. **[verified 2026-09-08] The checkers themselves.** Seventeen of them now gate every commit,
   and a checker that cannot fail is worse than no checker — it converts an unexamined area into
   a green tick. Two shipped guards were found green-on-red in 2026-09 (§3). A new checker is
   not trusted until it has been observed going RED on a deliberately broken input.

## 4. Risk tiers

Work is classified before it is done, and higher tiers need more evidence.

**Tier 0 — provably dead.** Unreachable code, unreferenced private members,
unused fields, warnings. Evidence: compiler + full-repo grep + a check against
the reflection/Harmony surface (see §5). Guard: the removed member disappears,
nothing else changes.

**Tier 1 — pure motion.** Splitting files, moving types, partial classes,
reordering members, renaming internals, extracting a method whose body is moved
verbatim. Statement order is preserved exactly. Guard: empty, or confined to the
touched type with only name/structure differences.

**Tier 2 — deduplication.** Merging code that is genuinely identical into one
helper. The danger is code that is *nearly* identical because one copy carries a
hard-won difference. Evidence required: a side-by-side diff of the copies in the
commit message, proving the difference is nil. If a difference exists, **do not
merge** — document why they differ instead.

**Tier 3 — behavioural.** Changed ordering, timing, numbers, allocation
patterns, frame semantics. **Not part of this refactor.** Anything found that
would need it is written into `PLAN.md` as a proposal for the user to decide,
and left in place.

## 5. What "unused" must be checked against

C# "no references" is not sufficient in this codebase. Before deleting anything,
check it is not:

- a **Harmony patch** target or an annotated patch method (`docs/PATCH-INVENTORY.md`),
- a Unity **message** (`Awake/Start/Update/LateUpdate/OnDestroy/OnEnable/…`) or a
  serialized field,
- reached by **reflection** or by name from the game's assemblies,
- a **config entry** — an unread config key is still a user's persisted setting,
  and removing it silently changes behaviour on their machine,
- a **log grep token** the user or the debug docs rely on,
- referenced only from the **debug menu**, which is a deliberate feature.

## 6. Phases

| Phase | Output | State |
|---|---|---|
| 0 | Charter, guard harness, baseline | **done** |
| 1 | Per-subsystem code review → `REVIEW-*.md`; invariant registry → the four `INVARIANTS-*.md` | **done** |
| 2 | Dead-code and duplication census, cross-checked against §5 | **done** |
| 3 | `PLAN.md`: ordered, tiered work list with a guard expectation per item | **done** |
| 4 | Execution — small commits, guard-checked, one subsystem at a time | **done** |
| 5 | Final report + a regression test script for the user's hardware pass | **done** |

Phase 4 does not begin until the user has seen the plan from Phase 3.

**[verified 2026-09-08] This table is the FIRST programme (2026-07) only.** Two more have run
since, and a successor who reads "done" here and stops will miss both:

| Programme | Where it lives | State |
|---|---|---|
| 2026-07 (this table) | `PLAN.md`, `LOG.md`, `REVIEW-*.md`, `INVARIANTS-*.md` | done |
| 2026-08 (splitting) | `PLAN-2026-08.md`, `LOG-2026-08.md`, `INVARIANTS-2026-08-SplitTargets.md`, `census-2026-08/` | done |
| 2026-09 (review + fix) | `.planning/refactor-2026-09/` — `BRIEF.md` first, then `REVIEW-<lane>.md`, `NEEDED-OUTSIDE-<lane>.md`, `HARDWARE-REGRESSION-2026-09.md` | done |

**`BRIEF.md` in `.planning/refactor-2026-09/` amends this charter for that round** and says so in
its own §1: Tier 3 was lifted for source-demonstrable defects only, parallel construction was put
in scope, and comments were declared load-bearing rather than clutter. Read it before assuming
§4's tiers are the whole rule set.

## 7. Working rules

- One subsystem per commit. Never mix tiers in a commit.
- Build stays green at every commit; guard output recorded at every commit.
- Parallel workers get `isolation: worktree` — they collide in a shared checkout.
- No game data is modified; every game-object mutation stays reversible.
- Every feature stays multiplayer-compatible; the wire format is frozen.
- When a piece of logic is understood, its *reason* is written down — as a
  comment at the code or an entry in `INVARIANTS.md`. Recovering that reason is
  most of the value of this exercise.
