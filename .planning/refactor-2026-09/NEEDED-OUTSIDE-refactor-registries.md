# NEEDED OUTSIDE — lane `refactor-registries` (documentation audit, 2026-09-08)

> Lane scope: `.planning/refactor/*.md` and `.planning/refactor-2026-09/*.md`, nothing else.
> This lane changed **no source**. Everything below is a change this lane found and verified but
> may not make, with the exact edit and the evidence for it.
>
> Base: `49ceab21` (ModBuild 483). Every claim here was checked against source at that commit.

---

## 1. `src/GloomhavenVR/Core/Water/WaterReflectionCaps.cs:38` — a cref that can be restored

**What.** `<c>Classify</c>` should be `<see cref="WaterReflectionCaps.Classify"/>`.

**Why this is not archaeology.** `STALE-DOC-REFS.md` has carried this since 2026-08-27 as a
"reference to a symbol that no longer exists". **The symbol exists** and always did:

```
src/GloomhavenVR/Core/Water/WaterReflectionCaps.cs:127
    internal static WaterCapFamily Classify(string? propertyName)
```

added at ModBuild 160 (`a7b802f5`), i.e. before the list that calls it dangling was produced.
The cref did not fail because the member was missing. It failed because **the doc block it sits
in documents a different type**: the block spans roughly lines 6–58 and closes on
`internal enum WaterCapFamily` (`:59`), whereas `Classify` is a member of
`internal static class WaterReflectionCaps` (`:79`). An unqualified cref cannot bind across
that boundary. The same block already gets this right two lines earlier with
`<see cref="WaterReflectionCaps.SelectorTokens"/>`, and inside the class the plain
`<see cref="Classify"/>` at `:163` resolves fine.

**Exact diff:**

```diff
--- a/src/GloomhavenVR/Core/Water/WaterReflectionCaps.cs
+++ b/src/GloomhavenVR/Core/Water/WaterReflectionCaps.cs
@@ -36,7 +36,7 @@
 /// floored at <c>1 - smoothnessCap</c>. A property whose name says BOTH (something matching
 /// "rough" and "gloss" at once) has no readable direction at all and is refused outright rather
-/// than guessed at — <c>Classify</c> returns <see cref="WaterCapFamily.None"/> and the
+/// than guessed at — <see cref="WaterReflectionCaps.Classify"/> returns <see cref="WaterCapFamily.None"/> and the
 /// caller logs the refusal by name, so the next hardware log can name what we declined to
 /// touch.</para>
```

**Risk.** None to behaviour: an XML doc comment does not reach the assembly (`CHARTER.md` §3
measured 0 `<summary>` tags in the whole `ilspycmd` snapshot). The guard must report *nothing*.
The only way this can fail is a build error if the cref does not bind, which
`GenerateDocumentationFile` will say at once (CS1574).

**Owner.** Lane `core` (`src/GloomhavenVR/Core/`) under the 2026-09 split.

**After applying**, delete the `Classify` row from `STALE-DOC-REFS.md`'s retired table — it will
then be true in both directions.

---

## 2. Nothing else.

No other source change is required by this audit. Two things were deliberately left as
**findings rather than diffs**, because each needs a decision this lane may not make:

- **`SnapTurn.Update`, six early returns that do not re-arm `_armed`.** Written up in full in
  `INVARIANTS-Net-Rig.md` under "The MODE gate re-arms on the way out". The `WorldGrab` return
  (#5) is the one worth measuring. Adding a re-arm changes turn behaviour on a hardware-tuned
  feature: **Tier 3**, and a hardware question, not a refactor.
- Any correction to a comment inside `src/` that this audit's registry edits imply. This lane
  fixed the registry entries; where the *source comment* is also wrong, that is the owning
  lane's edit, not this one's.
