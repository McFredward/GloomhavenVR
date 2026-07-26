# Refactoring Charter

> The rules this refactor operates under. Written before any code was touched.
> Companion documents: `INVARIANTS.md` (what must not change), `REVIEW-*.md`
> (per-subsystem findings), `PLAN.md` (the ordered work list), `LOG.md` (what was
> actually done, commit by commit).

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

Because `-p` groups output **by namespace and type**, the snapshot is completely
independent of our source file layout. That yields the property this refactor
is built on:

| Refactor kind | Expected guard output |
|---|---|
| Move a type to another file, split a file, reorder members | **empty diff** |
| Rename a private member | diff confined to that one type, names only |
| Extract a method | diff confined to that one type |
| Anything appearing in a type I did not intend to touch | **collateral damage — revert** |

Verified: two builds of identical source differ only in the build timestamp,
which the script masks. A no-op check reports 0 changed lines.

This does not prove a refactor is *correct*. It proves the exact blast radius,
which is the thing that is otherwise invisible and is how regressions get in.

**Rule: every refactor commit records its guard output in `LOG.md`.**

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
| 0 | Charter, guard harness, baseline | done |
| 1 | Per-subsystem code review → `REVIEW-*.md`; invariant registry → `INVARIANTS.md` | |
| 2 | Dead-code and duplication census, cross-checked against §5 | |
| 3 | `PLAN.md`: ordered, tiered work list with a guard expectation per item | |
| 4 | Execution — small commits, guard-checked, one subsystem at a time | |
| 5 | Final report + a regression test script for the user's hardware pass | |

Phase 4 does not begin until the user has seen the plan from Phase 3.

## 7. Working rules

- One subsystem per commit. Never mix tiers in a commit.
- Build stays green at every commit; guard output recorded at every commit.
- Parallel workers get `isolation: worktree` — they collide in a shared checkout.
- No game data is modified; every game-object mutation stays reversible.
- Every feature stays multiplayer-compatible; the wire format is frozen.
- When a piece of logic is understood, its *reason* is written down — as a
  comment at the code or an entry in `INVARIANTS.md`. Recovering that reason is
  most of the value of this exercise.
