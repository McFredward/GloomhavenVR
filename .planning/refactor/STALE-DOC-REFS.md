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

7 references. The 2026-09 refactor cleared twenty-nine of the thirty-six: one from lane cards,
eight from lane worldui-front, eight from lane net and twelve from lane worldui-frame. The
LINE NUMBERS in the rows below are not maintained and several are weeks stale — locate a row
by its symbol, never by its number.

| file | line | symbol | the sentence that may no longer be true |
|---|---|---|---|
| `Core/Haunt/Haunt.cs` | 373 | `Schedule` | /// preference: <c>Schedule</c> partitions slots into <c>Groups</c> = 3 and picks within a |
| `Core/Perf/PerfTextureCensus.cs` | 114 | `Append` | /// only between <see cref="Begin"/> and <c>Append</c> inside a single window, and both ends |
| `Core/WallFade/WallSegmentFade.PropUnit.cs` | 255 | `BeginStandingPropScope` | /// <para>LIFETIME. Cleared once per commit in <c>BeginStandingPropScope</c> — the |
| `Core/WallFade/WallSegmentFade.PropUnit.cs` | 267 | `BeginStandingPropScope` | /// <summary>True only between <c>BeginStandingPropScope</c> (the first scope of a |
| `Core/WallFade/WallSegmentFade.PropUnit.cs` | 275 | `BeginStandingPropScope` | /// Called from <c>BeginStandingPropScope</c> at the top of every commit.</summary> |
| `Core/WallFade/WallSegmentFade.PropUnit.cs` | 800 | `BeginStandingPropScope` | /// <c>BeginStandingPropScope</c> at the top of the rescan (the standing rule's |
| `Core/Water/WaterReflectionCaps.cs` | 38 | `Classify` | /// than guessed at — <c>Classify</c> returns <see cref="WaterCapFamily.None"/> and the |


