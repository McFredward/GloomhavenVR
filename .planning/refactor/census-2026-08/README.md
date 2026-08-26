# Census tools for `PLAN-2026-08.md`

Every number in §0 of the plan came out of one of these. They are committed so the census can be
re-run against any commit rather than believed — including by whoever reads the plan after the
tree has moved on again.

They are **prototypes, not checkers**. Two of them become proper checkers in `scripts/` during
Phase 1 of the plan (`loadbearing.py` → the instrument/mechanism report, and the partial-part
compile-order rule that has no prototype here yet). The rest exist to produce a ranking to read.

Run from the repo root:

```bash
python3 .planning/refactor/census-2026-08/hygiene.py  src/GloomhavenVR   # types, raw spans, nesting, params
python3 .planning/refactor/census-2026-08/hygiene2.py src/GloomhavenVR   # methods ranked by CODE lines
python3 .planning/refactor/census-2026-08/dupes2.py   src/GloomhavenVR 12
python3 .planning/refactor/census-2026-08/layers.py   src/GloomhavenVR
python3 .planning/refactor/census-2026-08/loadbearing.py src/GloomhavenVR
```

`hygiene_lib.py` is the shared brace-depth walker. It is not a C# parser; it is good enough to
rank things and no better, and every tool here prints a reading list rather than a verdict.

## Known limits, because a tool that hides them is worse than no tool

- **`hygiene.py` ranks by raw span**, which in a tree that is 45 % comment measures documentation.
  It also mis-reads multi-line attributes as signatures (`Plugin.cs` "998-line `BepInPlugin`").
  **Use `hygiene2.py` for any judgement about size.**
- **`dupes2.py`** folds string literals to a digest instead of deleting them, and drops lines with
  no alphanumeric content. Both corrections were forced by its first version reporting 499 groups
  of which the top twenty were false — see the plan, §0.2.
- **`layers.py`** attributes a type to the directory that declares it, textually. Short type names
  that collide with ordinary identifiers (`Pair`, `Sample`, `Mode`, `Gate`, `Card`, `Local`,
  `Probe`) inflate their edges. Read the named types before believing an edge.
- **`loadbearing.py` over-reports on purpose**, and beyond that has two known systematic false
  positives: a chained reset `_a = _b = _c = 0;` counts `_b` and `_c` as reads, and a reset helper
  whose name does not match the diagnostic pattern (`ResetReport`) has its writes counted as
  outside reads. Its output is an upper bound and a reading list.
