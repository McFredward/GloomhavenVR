# Documentation references to symbols that no longer exist

> Produced 2026-08-27, when `GenerateDocumentationFile` was turned on for the first time and the
> compiler could finally resolve crefs (see `LOG-2026-08.md` §3.2).
>
> **What these are.** Each line was a `<see cref="X"/>` naming a symbol that is not declared
> anywhere in `src/`. The tag has been demoted to `<c>X</c>`, so the comment no longer CLAIMS the
> symbol is live — but the sentence around it usually still describes what X did, and that
> sentence is what actually needs checking. Rewriting it means finding out what took the job over,
> which is archaeology per site rather than a rename.
>
> **Why this file exists rather than a silent demotion.** Demoting the tag removes the site from
> CS1574, so without this list the debt would simply become invisible. The compiler check stays
> live for NEW dangling crefs, which is the part that keeps working on its own.
>
> **How to clear a line:** find what replaced the symbol, fix the sentence, and delete the line.

**Last verified 2026-09-08 against `49ceab21` (ModBuild 483).** Every one of the seven rows was
re-checked against source. **Five were wrong**: four had already been cleared and one names a
symbol that exists. Two survive, and each now carries the answer to "what replaced it", which is
the archaeology the header above says is the expensive part — so the next reader spends the edit,
not the search.

The 2026-09 refactor cleared twenty-nine of the original thirty-six: one from lane cards, eight
from lane worldui-front, eight from lane net and twelve from lane worldui-frame. This pass
retires four more. The LINE NUMBERS below are checked at the verification date above and go stale
immediately — locate a row by its symbol, never by its number.

## Open — 2 references

| file | line | symbol | the sentence, and what actually took the job over |
|---|---|---|---|
| `Core/Haunt/Haunt.cs` | 376 | `Schedule` | /// preference: `<c>Schedule</c>` partitions slots into `<c>Groups</c>` = 3 and picks within a … **Verified: the sentence is TRUE and the symbol never existed.** `Schedule` is a FILE-name suffix — `Core/Haunt/Haunt.Schedule.cs`, a second part of `internal static partial class Haunt`, not a type or member. The method is **`Haunt.Resolve(float clock, SkyStyle style)`** (`Haunt.Schedule.cs:180`); `Groups` is real (`Haunt.Schedule.cs:77`, `private const float Groups = 3f`) but is private to that part. `AssertHauntCards` in the same sentence is also correctly a `<c>` and must stay one: it lives in the Unity editor bake (`unity/GloomhavenVR.Assets/Assets/Editor/BuildEnvironmentRooms.cs:10526`), outside this assembly, so no cref can ever resolve it. |
| `Core/Perf/PerfTextureCensus.cs` | 114 | `Append` | /// only between `<see cref="Begin"/>` and `<c>Append</c>` inside a single window, and both ends … **Verified: the sentence is TRUE and `Append` is not a member of this type.** The class has no `Append`; the only `Append`s in the file are `StringBuilder.Append` and the four private `Append*` writers (`AppendGlobals`, `AppendPopulation`, `AppendMedian`, `AppendSoftest`, `AppendOtherTextures`, `AppendVerdict`). The window's far end is **`Log()`** (`PerfTextureCensus.cs:378`), whose `finally` calls `Reset()` (`:953`), and that is what clears `Surfaces`/`SurfaceIndex`/`Ranked`/`RatioScratch`. `Begin` (`:208`) calls `Reset()` first, so "both ends clear them" is exactly right — only the name is wrong. |

## Retired 2026-09-08 — 5 references

Kept as a record rather than deleted, because "this row was checked and is gone" is a different
statement from "this row was never here", and the next audit should not re-derive it.

| file | symbol | why it is retired |
|---|---|---|
| `Core/WallFade/WallSegmentFade.PropUnit.cs` (4 rows: 255, 267, 275, 800) | `BeginStandingPropScope` | **The symbol appears nowhere in `src/` and neither do the four sentences.** All four cited line numbers now hold unrelated text (the holder-chain arena at 255–275, the orphan-guard `<para>` at ~800). The 2026-09 worldui-frame lane rewrote this file; the rows were not retired with it. Nothing to do. |
| `Core/Water/WaterReflectionCaps.cs` (line 38) | `Classify` | **The symbol EXISTS** — `internal static WaterCapFamily Classify(string? propertyName)`, `WaterReflectionCaps.cs:127`, added at ModBuild 160 (`a7b802f5`), i.e. it already existed when this list was produced. This row was a mis-triage of the *reason* the cref failed. The doc block at line 38 documents **`internal enum WaterCapFamily`** (`:59`), a different type from **`internal static class WaterReflectionCaps`** (`:79`), so an UNQUALIFIED cref cannot resolve from it. The same block already gets this right two lines up with `<see cref="WaterReflectionCaps.SelectorTokens"/>`, and `<see cref="Classify"/>` resolves fine from inside the class at `:163`. **The fix is a qualification, not archaeology** — see `NEEDED-OUTSIDE-refactor-registries.md`. |
