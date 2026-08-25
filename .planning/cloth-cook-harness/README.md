# cloth-cook-harness

A standalone Unity 2021.3.5f1 Linux player that measures `UnityEngine.Cloth` behaviour outside the
game, so that claims in `src/GloomhavenVR/Board/FigureGrab/FigureCloth.cs` are measurements rather
than reasoning. Build and run instructions are at the end of `.planning/CLOTH-DURING-SCALE.md`.

## THIS DIRECTORY SUPERSEDES `.planning/perf/cloth-cook-harness/`

That directory holds the ModBuild 284–286 copies of `Builder.cs`, `ClothBench.cs`,
`ColliderSyncBench.cs`, `CookCostBench.cs` and `ScaleShapeBench.cs`. The copies here are those files
plus two fixes and three new benches, and **running the old copies will silently produce the wrong
thing**: `ClothBench` there boots by default and calls `Application.Quit`, so any `--bench=` run
against it exits 0 with none of the requested output. The old directory should be deleted; this lane
did not own it and therefore did not.

## The benches

| flag | file | what it answers |
|---|---|---|
| *(none)* | `ClothBench.cs` | the ModBuild 284 end-to-end algorithm comparison |
| `--bench=cost` | `CookCostBench.cs` | what each individual Cloth call costs |
| `--bench=sync` | `ColliderSyncBench.cs` | whether assigning collider arrays cooks |
| *(none)* | `ScaleShapeBench.cs` | the SETTLED shape across a rescale, ModBuild 286 arms |
| `--bench=drivers` | `CookDriversBench.cs` | **what actually drives the cost of a cook** — one Cloth property at a time at a fixed vertex count, plus a live-property block |
| `--bench=dir` | `DirectionArmsBench.cs` | **both directions** of a rescale, with a NULL and a POSITIVE control, self-intersection counts and rendered wireframes |
| `--bench=gesture` | `GestureBench.cs` | **the two state machines over a whole gesture**, sampled every frame |

## Two traps

* `ClothBench` and `ScaleShapeBench` boot by DEFAULT. `ClothBench` now stands down for any
  `--bench=` flag; `ScaleShapeBench` carries an explicit list, so **a new bench must add its flag
  there** or it will be pre-empted and the run will look like a clean success.
* `-batchmode -nographics` segfaults this player before the first scene loads. Run under
  `xvfb-run -a` with `-batchmode` only.

`shots/` holds the contact sheets referenced from `.planning/CLOTH-DURING-SCALE.md`; the benches
regenerate the individual PNGs into a `shots/` directory next to the built player.
